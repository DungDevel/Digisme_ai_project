using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IRoomService
{
    Task<PagedRoomResponse> GetRoomsAsync(string? q, RoomStatus? status, int page, int pageSize, bool includeInactive = false);
    Task<RoomResponse?> GetRoomByIdAsync(int id);
    Task<RoomResponse> CreateRoomAsync(int userId, RoomCreateRequest req);
    Task<RoomResponse> UpdateRoomAsync(int userId, int id, RoomUpdateRequest req);
    Task<RoomResponse> CheckInRoomAsync(int userId, int id, RoomCheckInRequest req);
    Task<RoomCheckOutResponse> CheckOutRoomAsync(int userId, int id);
    Task<bool> DeleteRoomAsync(int userId, int id);
}

public class RoomService : IRoomService
{
    private readonly AppDbContext _db;

    public RoomService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedRoomResponse> GetRoomsAsync(string? q, RoomStatus? status, int page, int pageSize, bool includeInactive = false)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Rooms.AsNoTracking().Include(x => x.CurrentOrder).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(x => x.Name.Contains(q) || x.Code.Contains(q));
        }

        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(MapToResponse).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedRoomResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<RoomResponse?> GetRoomByIdAsync(int id)
    {
        var room = await _db.Rooms.AsNoTracking().Include(x => x.CurrentOrder).FirstOrDefaultAsync(x => x.Id == id);
        return room is null ? null : MapToResponse(room);
    }

    public async Task<RoomResponse> CreateRoomAsync(int userId, RoomCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            throw new BusinessRuleException("Mã phòng không được để trống");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên phòng không được để trống");
        if (req.HourlyRate < 0m)
            throw new BusinessRuleException("Đơn giá giờ không được âm");

        var code = req.Code.Trim();
        var exists = await _db.Rooms.AnyAsync(x => x.Code.ToLower() == code.ToLower());
        if (exists)
            throw new BusinessRuleException("Mã phòng đã tồn tại");

        var room = new Room
        {
            Code = code,
            Name = req.Name.Trim(),
            HourlyRate = req.HourlyRate,
            Status = RoomStatus.Available,
            IsActive = true
        };

        _db.Rooms.Add(room);
        PosLogService.Write(_db, "ROOM_CREATED", userId, "room", room.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(room);
    }

    public async Task<RoomResponse> UpdateRoomAsync(int userId, int id, RoomUpdateRequest req)
    {
        var room = await _db.Rooms.Include(x => x.CurrentOrder).FirstOrDefaultAsync(x => x.Id == id);
        if (room is null)
            throw new KeyNotFoundException("Không tìm thấy phòng");

        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên phòng không được để trống");
        if (req.HourlyRate < 0m)
            throw new BusinessRuleException("Đơn giá giờ không được âm");

        room.Name = req.Name.Trim();
        room.HourlyRate = req.HourlyRate;
        room.IsActive = req.IsActive;

        PosLogService.Write(_db, "ROOM_UPDATED", userId, "room", room.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(room);
    }

    public async Task<RoomResponse> CheckInRoomAsync(int userId, int id, RoomCheckInRequest req)
    {
        var room = await _db.Rooms.Include(x => x.CurrentOrder).FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (room is null)
            throw new KeyNotFoundException("Không tìm thấy phòng");

        if (room.Status != RoomStatus.Available)
            throw new BusinessRuleException("Phòng không ở trạng thái trống để check-in");

        room.Status = RoomStatus.Occupied;
        room.StartTime = DateTime.UtcNow;

        PosLogService.Write(_db, "ROOM_CHECKIN", userId, "room", room.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(room);
    }

    public async Task<RoomCheckOutResponse> CheckOutRoomAsync(int userId, int id)
    {
        var room = await _db.Rooms.Include(x => x.CurrentOrder).FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (room is null)
            throw new KeyNotFoundException("Không tìm thấy phòng");

        if (room.Status != RoomStatus.Occupied || !room.StartTime.HasValue)
            throw new BusinessRuleException("Phòng không đang được sử dụng");

        var now = DateTime.UtcNow;
        var elapsedMinutes = (decimal)(now - room.StartTime.Value).TotalMinutes;
        if (elapsedMinutes < 1m) elapsedMinutes = 1m; // tối thiểu 1 phút

        var hoursUsed = elapsedMinutes / 60m;
        var amount = Math.Round(hoursUsed * room.HourlyRate, 0, MidpointRounding.AwayFromZero);

        int orderId = room.CurrentOrderId ?? 0;
        string orderCode = room.CurrentOrder?.Code ?? "ROOM-FEE";

        room.Status = RoomStatus.Available;
        room.StartTime = null;
        room.CurrentOrderId = null;
        room.CurrentOrder = null;

        PosLogService.Write(_db, "ROOM_CHECKOUT", userId, "room", $"{room.Code} - {elapsedMinutes} mins - {amount} VND");
        await _db.SaveChangesAsync();

        return new RoomCheckOutResponse(room.Id, room.Code, elapsedMinutes, hoursUsed, amount, orderId, orderCode);
    }

    public async Task<bool> DeleteRoomAsync(int userId, int id)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(x => x.Id == id);
        if (room is null)
            return false;

        if (room.Status == RoomStatus.Occupied)
            throw new BusinessRuleException("Không thể xóa phòng đang có khách sử dụng");

        _db.Rooms.Remove(room);
        PosLogService.Write(_db, "ROOM_DELETED", userId, "room", room.Code);
        await _db.SaveChangesAsync();
        return true;
    }

    private static RoomResponse MapToResponse(Room r) => new(
        r.Id, r.Code, r.Name, r.HourlyRate, r.Status, r.StartTime, r.CurrentOrderId, r.CurrentOrder?.Code, r.IsActive
    );
}
