using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// SPRINT 3. REPORTS (BÁO CÁO NÂNG CAO, KÊ KHAI THUẾ, TỒN LÂU & SỔ QUỸ)
/// - GET /api/v1/reports/sales         — Báo cáo tổng quan doanh số (ManagerOnly)
/// - GET /api/v1/reports/top-products  — Báo cáo sản phẩm bán chạy (ManagerOnly)
/// - GET /api/v1/reports/payment-methods — Báo cáo doanh thu theo phương thức thanh toán (ManagerOnly)
/// - GET /api/v1/reports/tax-declaration — Báo cáo kê khai thuế bán ra/mua vào (ManagerOnly)
/// - GET /api/v1/reports/advanced-dashboard — Dashboard nâng cao (Doanh thu, Lãi gộp, Tồn kho thấp, Sổ quỹ) (ManagerOnly)
/// - GET /api/v1/reports/slow-moving   — Báo cáo sản phẩm tồn lâu / bán chậm (ManagerOnly)
/// - GET /api/v1/reports/cash-flow     — Báo cáo thu/chi chi tiết / Sổ quỹ tổng hợp (ManagerOnly)
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = "ManagerOnly")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("sales")]
    [ProducesResponseType(typeof(SalesReportResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sales([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var result = await _reportService.GetSalesReportAsync(from, to);
        return Ok(result);
    }

    [HttpGet("top-products")]
    [ProducesResponseType(typeof(TopProductsReportResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TopProducts([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int top = 10)
    {
        if (top is < 1 or > 100) top = 10;
        var result = await _reportService.GetTopProductsAsync(from, to, top);
        return Ok(result);
    }

    [HttpGet("payment-methods")]
    [ProducesResponseType(typeof(PaymentMethodsReportResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PaymentMethods([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var result = await _reportService.GetPaymentMethodsReportAsync(from, to);
        return Ok(result);
    }

    [HttpGet("tax-declaration")]
    [ProducesResponseType(typeof(TaxDeclarationReportResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TaxDeclaration([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var result = await _reportService.GetTaxDeclarationAsync(from, to);
        return Ok(result);
    }

    [HttpGet("advanced-dashboard")]
    [ProducesResponseType(typeof(AdvancedDashboardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdvancedDashboard([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var result = await _reportService.GetAdvancedDashboardAsync(from, to);
        return Ok(result);
    }

    [HttpGet("slow-moving")]
    [ProducesResponseType(typeof(SlowMovingReportResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SlowMoving([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var result = await _reportService.GetSlowMovingProductsAsync(from, to);
        return Ok(result);
    }

    [HttpGet("cash-flow")]
    [ProducesResponseType(typeof(CashFlowReportResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CashFlow([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var result = await _reportService.GetCashFlowReportAsync(from, to);
        return Ok(result);
    }
}
