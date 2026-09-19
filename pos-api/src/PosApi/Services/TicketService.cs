using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using PosApi.Domain;

namespace PosApi.Services;

public class JwtOptions
{
    public string Issuer { get; set; } = "DigiSME.PosApi";
    public string Audience { get; set; } = "DigiSME.Frontend";
    public string SecretKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 480;
    public int ResetCodeMinutes { get; set; } = 30;
}

public interface ITicketService
{
    string IssueAccessToken(User user);
    (string plainCode, string hashedCode) GenerateResetCode();
}

public class TicketService : ITicketService
{
    private readonly JwtOptions _opts;
    private readonly SymmetricSecurityKey _key;

    public TicketService(IOptions<JwtOptions> opts)
    {
        _opts = opts.Value;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SecretKey));
    }

    public string IssueAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("full_name", user.FullName),
            new(ClaimTypes.Role, user.Role.ToString()),
        };
        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string plainCode, string hashedCode) GenerateResetCode()
    {
        // Mã 6 chữ số, in được ra màn hình (đồ án không có email/SMS gateway)
        var rnd = RandomNumberGenerator.Create();
        Span<byte> buf = stackalloc byte[3];
        rnd.GetBytes(buf);
        int code = (buf[0] << 16 | buf[1] << 8 | buf[2]) % 1_000_000;
        string plain = code.ToString("D6");
        return (plain, Hashing.Hash(plain));
    }
}

public static class Hashing
{
    public static string Hash(string value)
    {
        var bytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(value),
            Encoding.UTF8.GetBytes("DigiSME.pos.api.salt.v1"),
            iterations: 300_000,
            HashAlgorithmName.SHA256,
            32);
        return Convert.ToBase64String(bytes);
    }
}
