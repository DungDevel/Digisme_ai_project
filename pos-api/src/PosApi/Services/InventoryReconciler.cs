using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using Microsoft.Extensions.Logging;

namespace PosApi.Services;

public static class InventoryReconciler
{
    private static decimal Round3(decimal val) => Math.Round(val, 3, MidpointRounding.AwayFromZero);

    public static async Task<ReconcileReportResult> ComputeDiffsAsync(AppDbContext context, int? targetWarehouseId = null)
    {
        Warehouse? defaultWarehouse = null;
        if (targetWarehouseId.HasValue)
        {
            defaultWarehouse = await context.Warehouses.FindAsync(targetWarehouseId.Value);
        }

        defaultWarehouse ??= await context.Warehouses.FirstOrDefaultAsync(w => w.IsDefault && w.IsActive)
            ?? await context.Warehouses.FirstOrDefaultAsync(w => w.IsActive);

        if (defaultWarehouse == null)
        {
            throw new InvalidOperationException("Không tìm thấy kho mặc định hoặc kho hoạt động nào để thực hiện cân bằng tồn kho (Q14). Vui lòng cấu hình ít nhất một kho hoạt động.");
        }

        var products = await context.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync();
        var details = new List<ReconcileItemDetail>();
        int surplusCount = 0;
        int deficitCount = 0;

        foreach (var p in products)
        {
            var totalWhStock = await context.WarehouseStocks
                .AsNoTracking()
                .Where(ws => ws.ProductId == p.Id)
                .SumAsync(ws => (decimal?)ws.Stock) ?? 0m;

            var diff = Round3(p.CurrentStock - totalWhStock);
            if (diff != 0m)
            {
                var actionType = diff > 0m ? "WillAddToDefaultWarehouse" : "ReportOnly";
                if (diff > 0m) surplusCount++;
                else deficitCount++;

                details.Add(new ReconcileItemDetail(
                    p.Id,
                    p.Barcode,
                    p.Name,
                    p.CurrentStock,
                    totalWhStock,
                    diff,
                    actionType
                ));
            }
        }

        return new ReconcileReportResult(defaultWarehouse.Id, defaultWarehouse.Name, products.Count, surplusCount, deficitCount, details);
    }

    public static async Task ReconcileAsync(AppDbContext context, int? targetWarehouseId = null, ILogger? logger = null)
    {
        Warehouse? defaultWarehouse = null;
        if (targetWarehouseId.HasValue)
        {
            defaultWarehouse = await context.Warehouses.FindAsync(targetWarehouseId.Value);
        }

        defaultWarehouse ??= await context.Warehouses.FirstOrDefaultAsync(w => w.IsDefault && w.IsActive)
            ?? await context.Warehouses.FirstOrDefaultAsync(w => w.IsActive);

        if (defaultWarehouse == null)
        {
            throw new InvalidOperationException("Không tìm thấy kho mặc định hoặc kho hoạt động nào để thực hiện cân bằng tồn kho (Q14). Vui lòng cấu hình ít nhất một kho hoạt động.");
        }

        // d. Exclude inactive products (discontinued/inactive items should not have active inventory reconciled)
        var products = await context.Products.Where(p => p.IsActive).ToListAsync();
        var timestampStr = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        foreach (var p in products)
        {
            var totalWhStock = await context.WarehouseStocks
                .Where(ws => ws.ProductId == p.Id)
                .SumAsync(ws => (decimal?)ws.Stock) ?? 0m;

            var diff = Round3(p.CurrentStock - totalWhStock);
            if (diff > 0m)
            {
                var whStock = await context.WarehouseStocks
                    .FirstOrDefaultAsync(ws => ws.WarehouseId == defaultWarehouse.Id && ws.ProductId == p.Id);

                if (whStock == null)
                {
                    whStock = new WarehouseStock
                    {
                        WarehouseId = defaultWarehouse.Id,
                        ProductId = p.Id,
                        Stock = diff,
                        UpdatedAt = DateTime.UtcNow
                    };
                    context.WarehouseStocks.Add(whStock);
                }
                else
                {
                    whStock.Stock = Round3(whStock.Stock + diff);
                    whStock.UpdatedAt = DateTime.UtcNow;
                }

                // a. Every WarehouseStock change must have a StockLedger entry with a unique RefId
                var refId = $"Q14-RECONCILE-{p.Id}-{timestampStr}";
                context.StockLedger.Add(new StockLedgerEntry
                {
                    ProductId = p.Id,
                    WarehouseId = defaultWarehouse.Id,
                    Qty = diff,
                    Balance = p.CurrentStock,
                    RefType = LedgerRefType.Adjustment,
                    RefId = refId,
                    Note = "Cân bằng số dư đầu kỳ (Q14)"
                });
            }
            else if (diff < 0m)
            {
                // b. CurrentStock < TotalWarehouseStock: log/list into PosLog, do not modify values
                PosLogService.Write(context, "Q14_UNDER_STOCK_MISMATCH", null, "product", p.Id.ToString(), new { p.Barcode, p.Name, p.CurrentStock, totalWhStock, diff });
                logger?.LogWarning("[Q14_UNDER_STOCK_MISMATCH] ProductId: {ProductId}, Name: {Name}, CurrentStock: {CurrentStock}, TotalWhStock: {TotalWhStock}, Diff: {Diff}", p.Id, p.Name, p.CurrentStock, totalWhStock, diff);
            }
        }

        await context.SaveChangesAsync();
    }
}

public record ReconcileReportResult(int TargetWarehouseId, string TargetWarehouseName, int TotalProductsChecked, int ProductsWithSurplusCount, int ProductsWithDeficitCount, List<ReconcileItemDetail> Details);
public record ReconcileItemDetail(int ProductId, string Barcode, string Name, decimal CurrentStock, decimal TotalWhStock, decimal Diff, string ActionType);

