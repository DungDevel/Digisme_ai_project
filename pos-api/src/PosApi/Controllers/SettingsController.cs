using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/settings")]
[Authorize(Policy = "AnyRole")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SettingsController(AppDbContext db) => _db = db;

    [HttpGet("store-profile")]
    public async Task<IActionResult> GetStoreProfile()
    {
        var setting = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        if (setting == null)
        {
            setting = new StoreSetting { Id = 1, StoreName = "DigiSME POS Store", StoreType = "Retail" };
            _db.StoreSettings.Add(setting);
            await _db.SaveChangesAsync();
        }

        bool isFAndB = string.Equals(setting.StoreType, "FandB", StringComparison.OrdinalIgnoreCase);

        return Ok(new {
            storeName = setting.StoreName,
            address = setting.Address,
            storeType = setting.StoreType,
            vietQrBankCode = setting.VietQrBankCode,
            vietQrAccountNo = setting.VietQrAccountNo,
            vietQrAccountName = setting.VietQrAccountName,
            vatRatePercent = setting.VatRatePercent,
            featureFlags = new {
                enableTables = isFAndB,
                enableRooms = isFAndB,
                enableRecipes = isFAndB,
                enableBarcode = !isFAndB,
                enableMultiWarehouse = true
            }
        });
    }

    [HttpPut("store-profile")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> UpdateStoreProfile([FromBody] StoreProfileUpdateRequest req)
    {
        var setting = await _db.StoreSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (setting == null)
        {
            setting = new StoreSetting { Id = 1 };
            _db.StoreSettings.Add(setting);
        }

        if (!string.IsNullOrWhiteSpace(req.StoreName)) setting.StoreName = req.StoreName.Trim();
        if (req.Address != null) setting.Address = req.Address.Trim();
        if (!string.IsNullOrWhiteSpace(req.StoreType))
        {
            var st = req.StoreType.Trim();
            if (st != "Retail" && st != "FandB")
                return BadRequest(new { error = "StoreType chỉ nhận 'Retail' hoặc 'FandB'" });
            setting.StoreType = st;
        }
        if (req.VietQrBankCode != null) setting.VietQrBankCode = req.VietQrBankCode.Trim();
        if (req.VietQrAccountNo != null) setting.VietQrAccountNo = req.VietQrAccountNo.Trim();
        if (req.VietQrAccountName != null) setting.VietQrAccountName = req.VietQrAccountName.Trim();
        if (req.VatRatePercent.HasValue) setting.VatRatePercent = req.VatRatePercent.Value;
        setting.UpdatedAt = DateTime.UtcNow;

        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        PosLogService.Write(_db, "STORE_PROFILE_UPDATED", userId, "settings", "1");
        await _db.SaveChangesAsync();

        return Ok(new { message = "Cập nhật cấu hình cửa hàng thành công", setting });
    }

    [HttpGet("stock-take-thresholds")]
    public async Task<IActionResult> GetStockTakeThresholds()
    {
        var setting = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        decimal autoLimit = setting?.StockTakeAutoLimit > 0 ? setting.StockTakeAutoLimit : 200000m;
        decimal managerLimit = setting?.StockTakeManagerLimit > 0 ? setting.StockTakeManagerLimit : 1000000m;
        return Ok(new StockTakeThresholdsResponse(autoLimit, managerLimit));
    }

    [HttpPut("stock-take-thresholds")]
    [Authorize(Policy = "ManagerOnly")]
    public async Task<IActionResult> UpdateStockTakeThresholds([FromBody] StockTakeThresholdsRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || !user.IsOwner)
            throw new BusinessRuleException("Chỉ Store Owner mới có quyền cấu hình ngưỡng duyệt kiểm kê");

        if (req.AutoLimit <= 0 || req.AutoLimit >= req.ManagerLimit)
            throw new BusinessRuleException("Ngưỡng tự động phải > 0 và nhỏ hơn ngưỡng Manager");

        var setting = await _db.StoreSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (setting == null)
        {
            setting = new StoreSetting { Id = 1 };
            _db.StoreSettings.Add(setting);
        }

        setting.StockTakeAutoLimit = req.AutoLimit;
        setting.StockTakeManagerLimit = req.ManagerLimit;
        setting.UpdatedAt = DateTime.UtcNow;

        PosLogService.Write(_db, "STOCK_TAKE_THRESHOLDS_UPDATED", userId, "settings", "1");
        await _db.SaveChangesAsync();

        return Ok(new { message = "Cập nhật ngưỡng duyệt kiểm kê thành công", autoLimit = setting.StockTakeAutoLimit, managerLimit = setting.StockTakeManagerLimit });
    }
}

public record StoreProfileUpdateRequest(
    string? StoreName,
    string? Address,
    string? StoreType,
    string? VietQrBankCode,
    string? VietQrAccountNo,
    string? VietQrAccountName,
    decimal? VatRatePercent
);
