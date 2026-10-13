using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PosApi.Data;
using System.Net.Http;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/health")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _aiUrl;
    private readonly HttpClient _httpClient;
    public HealthController(AppDbContext db, IConfiguration cfg, HttpClient httpClient)
    {
        _db = db;
        _aiUrl = cfg["AiService:BaseUrl"] ?? "http://localhost:8001";
        _httpClient = httpClient;
    }

    /// Liveness: API sống là đủ
    [HttpGet]
    public IActionResult Live()
    {
        return Ok(new { status = "ok", service = "pos-api", version = "0.1.0-phase0", time = DateTime.UtcNow });
    }

    /// Readiness: kiểm tra DB + ai-service
    [HttpGet("ready")]
    public async Task<IActionResult> Ready()
    {
        var dbOk = true;
        string? dbError = null;
        try { await _db.Database.CanConnectAsync(); }
        catch (Exception ex) { dbOk = false; dbError = ex.Message; }

        var aiOk = false;
        try
        {
            var response = await _httpClient.GetAsync(_aiUrl.TrimEnd('/') + "/health");
            aiOk = response.IsSuccessStatusCode;
        }
        catch { /* ai-service chưa chạy, timeout, hoặc lỗi kết nối */ }

        var healthy = dbOk; // ai-service là optional ở phase 0
        return Ok(new { status = healthy ? "ready" : "degraded", database = dbOk, databaseError = dbError, aiService = aiOk });
    }
}
