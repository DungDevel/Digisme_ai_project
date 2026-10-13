using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// M7. STOCK-IN / SUPPLIER INVOICES (OCR & Stock In Confirmation)
/// - POST /api/v1/stock-ins        — Upload ảnh hóa đơn OCR (Manager Only)
/// - GET  /api/v1/stock-ins/{id}   — Xem thông tin hóa đơn & kết quả OCR (Manager Only)
/// - PUT  /api/v1/stock-ins/{id}/items — Sửa items hóa đơn pending (Manager Only)
/// - POST /api/v1/stock-ins/{id}/confirm — Xác nhận nhập kho, cộng kho + ledger (Manager Only)
/// - DELETE /api/v1/stock-ins/{id} — Từ chối hóa đơn pending (Manager Only)
/// </summary>
[ApiController]
[Route("api/v1/stock-ins")]
[Authorize(Policy = "ManagerOnly")]
public class StockInsController : ControllerBase
{
    private readonly IStockInService _stockInService;

    public StockInsController(IStockInService stockInService)
    {
        _stockInService = stockInService;
    }

    [HttpPost]
    public async Task<IActionResult> UploadInvoice([FromForm] IFormFile file)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return Unauthorized();
        var userId = int.Parse(userIdClaim.Value);

        if (file == null || file.Length == 0)
            return BadRequest(new { error = file == null ? "File không được để trống" : "File không có dữ liệu" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".pdf"))
            return BadRequest(new { error = "Định dạng file không hỗ trợ (chỉ nhận .jpg, .jpeg, .png, .pdf)" });

        try
        {
            var invoice = await _stockInService.UploadInvoiceAsync(userId, file);
            return StatusCode(201, invoice);
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Lỗi upload hoặc OCR: {ex.Message}" });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetInvoice(int id)
    {
        try
        {
            var invoice = await _stockInService.GetInvoiceByIdAsync(id);
            return Ok(invoice);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}/items")]
    public async Task<IActionResult> UpdateItems(int id, [FromBody] List<StockInItemRequest> items)
    {
        try
        {
            var invoice = await _stockInService.UpdateInvoiceItemsAsync(id, items);
            return Ok(invoice);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, [FromBody] ConfirmStockInRequestDto? body)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return Unauthorized();
        var userId = int.Parse(userIdClaim.Value);

        var idempotencyKey = Request.Headers["X-Idempotency-Key"].FirstOrDefault();

        if (body == null || body.Items == null || body.Items.Count == 0)
        {
            return BadRequest(new { error = "Vui lòng cung cấp danh sách mặt hàng (items) cần xác nhận nhập kho" });
        }

        try
        {
            var req = new ConfirmStockInRequest(
                InvoiceId: id,
                Items: body.Items,
                SupplierName: body.SupplierName,
                Note: body.Note
            );

            var result = await _stockInService.ConfirmStockInAsync(userId, req, idempotencyKey);
            return StatusCode(201, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Reject(int id)
    {
        try
        {
            await _stockInService.RejectInvoiceAsync(id);
            return Ok(new { message = "Đã từ chối hóa đơn thành công" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record ConfirmStockInRequestDto(List<StockInItemRequest>? Items, string? SupplierName, string? Note);
