using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = "OwnerOnly")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "username, email, password là bắt buộc" });

        var username = req.Username.Trim();
        var email = req.Email.Trim();

        if (req.Password.Length < 8)
            return BadRequest(new { error = "Mật khẩu tối thiểu 8 ký tự" });

        if (!Enum.IsDefined(typeof(UserRole), req.Role))
            return BadRequest(new { error = "Vai trò (Role) không hợp lệ" });

        if (await _db.Users.AnyAsync(u => u.Username == username))
            return BadRequest(new { error = "Tên đăng nhập đã tồn tại" });

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return BadRequest(new { error = "Email đã được đăng ký" });

        var user = new User
        {
            Username = username,
            FullName = string.IsNullOrWhiteSpace(req.FullName) ? username : req.FullName.Trim(),
            Email = email,
            PasswordHash = Hashing.Hash(req.Password),
            Role = req.Role,
            IsOwner = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var response = new UserResponse(
            user.Id,
            user.Username,
            user.FullName,
            user.Email,
            user.Role,
            user.IsOwner,
            user.IsActive,
            user.CreatedAt
        );

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("{id:int}/reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetUserPasswordRequest req)
    {
        var targetUser = await _db.Users.FindAsync(id);
        if (targetUser is null)
            return NotFound(new { error = "Không tìm thấy người dùng" });

        var (valid, error) = PasswordPolicy.Validate(req.NewPassword);
        if (!valid)
            return BadRequest(new { error });

        targetUser.PasswordHash = Hashing.Hash(req.NewPassword);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Đặt lại mật khẩu thành công" });
    }
}
