using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// M7. STOCK-IN / SUPPLIER INVOICES (OCR & Stock In Confirmation)
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

    [HttpGet]
    [ProducesResponseType(typeof(PagedStockInResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] int? warehouseId,
        [FromQuery] int? supplierId,
        [FromQuery] StockInStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _stockInService.GetStockInsAsync(warehouseId, supplierId, status, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(StockInResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var stockIn = await _stockInService.GetStockInByIdAsync(id);
            if (stockIn == null)
            {
                // Fallback: check if it's an invoice
                var invoice = await _stockInService.GetInvoiceByIdAsync(id);
                return Ok(invoice);
            }
            return Ok(stockIn);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadInvoice(IFormFile file)
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

    [HttpPost("manual")]
    [ProducesResponseType(typeof(StockInResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateManual([FromBody] StockInCreateRequest req)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return Unauthorized();
        var userId = int.Parse(userIdClaim.Value);

        try
        {
            var result = await _stockInService.CreateStockInDraftAsync(userId, req);
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

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(StockInResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDraft(int id, [FromBody] StockInUpdateRequest req)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return Unauthorized();
        var userId = int.Parse(userIdClaim.Value);

        try
        {
            var result = await _stockInService.UpdateStockInDraftAsync(userId, id, req);
            return Ok(result);
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

        try
        {
            // Check if this id is a Draft StockIn or a SupplierInvoice
            var stockIn = await _stockInService.GetStockInByIdAsync(id);
            if (stockIn != null && stockIn.Status == StockInStatus.Draft)
            {
                var result = await _stockInService.ConfirmStockInDraftAsync(userId, id, idempotencyKey);
                return Ok(result);
            }

            // Otherwise treat as Confirm OCR invoice
            if (body == null || body.Items == null || body.Items.Count == 0)
            {
                return BadRequest(new { error = "Vui lòng cung cấp danh sách mặt hàng (items) cần xác nhận nhập kho" });
            }

            var req = new ConfirmStockInRequest(
                InvoiceId: id,
                Items: body.Items,
                SupplierName: body.SupplierName,
                Note: body.Note,
                SupplierId: body.SupplierId
            );

            var createdResp = await _stockInService.ConfirmStockInAsync(userId, req, idempotencyKey);
            return StatusCode(201, createdResp);
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

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return Unauthorized();
        var userId = int.Parse(userIdClaim.Value);

        try
        {
            var result = await _stockInService.CancelStockInAsync(userId, id);
            return Ok(result);
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
    public async Task<IActionResult> DeleteOrReject(int id)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return Unauthorized();
        var userId = int.Parse(userIdClaim.Value);

        try
        {
            // Try deleting draft stock-in first
            await _stockInService.DeleteStockInAsync(userId, id);
            return Ok(new { message = "Đã xóa phiếu nhập nháp thành công" });
        }
        catch (KeyNotFoundException)
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
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record ConfirmStockInRequestDto(List<StockInItemRequest>? Items, int? SupplierId, string? SupplierName, string? Note);
