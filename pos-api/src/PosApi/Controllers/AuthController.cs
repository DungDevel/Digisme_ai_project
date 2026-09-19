using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITicketService _tickets;
    private readonly JwtOptions _jwt;

    public AuthController(AppDbContext db, ITicketService tickets, IOptions<JwtOptions> jwt)
    {
        _db = db;
        _tickets = tickets;
        _jwt = jwt.Value;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "username, email, password là bắt buộc" });
        if (req.Password.Length < 8)
            return BadRequest(new { error = "Mật khẩu tối thiểu 8 ký tự" });

        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return BadRequest(new { error = "Tên đăng nhập đã tồn tại" });
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return BadRequest(new { error = "Email đã được đăng ký" });

        // Phase 0: mọi tài khoản tự đăng ký là Cashier; Manager do admin tạo trong DB/seed
        var user = new User
        {
            Username = req.Username,
            FullName = string.IsNullOrWhiteSpace(req.FullName) ? req.Username : req.FullName,
            Email = req.Email,
            PasswordHash = Hashing.Hash(req.Password),
            Role = UserRole.Cashier
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Register), new { }, ToAuthResponse(user));
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user is null || !user.IsActive || user.PasswordHash != Hashing.Hash(req.Password))
            return Unauthorized(new { error = "Sai tên đăng nhập hoặc mật khẩu" });

        return Ok(ToAuthResponse(user));
    }

    /// <summary>
    /// Phase 0: không có email/SMS gateway — mã reset được trả thẳng về client để demo luồng.
    /// Khi bảo vệ: giải thích có thể thay bằng gửi email (MailKit) hoặc SMS.
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ForgotPasswordResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
        // Không tiết lộ email có tồn tại hay không — luôn trả 200
        if (user is null)
            return Ok(new ForgotPasswordResponse("000000", _jwt.ResetCodeMinutes));

        // Hủy các mã cũ chưa dùng
        var stale = _db.PasswordResetCodes.Where(c => c.UserId == user.Id && c.UsedAt == null).ToList();
        _db.PasswordResetCodes.RemoveRange(stale);

        var (plain, hashed) = _tickets.GenerateResetCode();
        _db.PasswordResetCodes.Add(new PasswordResetCode
        {
            UserId = user.Id,
            CodeHash = hashed,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwt.ResetCodeMinutes)
        });
        await _db.SaveChangesAsync();

        return Ok(new ForgotPasswordResponse(plain, _jwt.ResetCodeMinutes));
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code) || req.NewPassword.Length < 8)
            return BadRequest(new { error = "Code bắt buộc; mật khẩu mới tối thiểu 8 ký tự" });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
        if (user is null) return BadRequest(new { error = "Email không tồn tại" });

        var code = await _db.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.UsedAt == null && c.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
        if (code is null || code.CodeHash != Hashing.Hash(req.Code))
            return BadRequest(new { error = "Mã không hợp lệ hoặc đã hết hạn" });

        user.PasswordHash = Hashing.Hash(req.NewPassword);
        code.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Đổi mật khẩu thành công" });
    }

    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Me()
    {
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var name = User.Identity?.Name;
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        return Ok(new { id = idClaim, username = name, fullName = User.FindFirst("full_name")?.Value, role });
    }

    private AuthResponse ToAuthResponse(User user)
    {
        var token = _tickets.IssueAccessToken(user);
        var exp = (long)(DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        return new AuthResponse(token, user.Username, user.FullName, user.Role, exp);
    }
}
