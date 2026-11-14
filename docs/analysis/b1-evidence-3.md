# Báo cáo Bằng chứng Kiểm định B1 (Vòng 3 — b1-evidence-3.md)

> **Branch hiện tại:** `feature/b1-stockin-warehouse`  
> **Ngày kiểm tra:** 2026-11-14  
> **Quy định:** Dán nguyên văn mã nguồn và đầu ra lệnh thực tế. Không suy đoán ("có thể").

---

### A. Q14: Cân bằng tồn kho (Inventory Reconciliation)

#### 1. Script chỉ đọc `tools/q14/preview-mismatch.sql`
```sql
-- preview-mismatch.sql: Preview mismatched products between Products.CurrentStock and SUM(WarehouseStocks.Stock)
WITH ProductStockSummary AS (
    SELECT 
        p."Id" AS "ProductId",
        p."Barcode",
        p."Name",
        p."CurrentStock",
        COALESCE(SUM(ws."Stock"), 0) AS "TotalWarehouseStock",
        p."CurrentStock" - COALESCE(SUM(ws."Stock"), 0) AS "Difference"
    FROM "Products" p
    LEFT JOIN "WarehouseStocks" ws ON ws."ProductId" = p."Id"
    GROUP BY p."Id", p."Barcode", p."Name", p."CurrentStock"
    HAVING p."CurrentStock" <> COALESCE(SUM(ws."Stock"), 0)
)
SELECT 
    'OVER_STOCK' AS "GroupType",
    "ProductId",
    "Barcode",
    "Name",
    "CurrentStock",
    "TotalWarehouseStock",
    "Difference"
FROM ProductStockSummary
WHERE "Difference" > 0

UNION ALL

SELECT 
    'UNDER_STOCK' AS "GroupType",
    "ProductId",
    "Barcode",
    "Name",
    "CurrentStock",
    "TotalWarehouseStock",
    "Difference"
FROM ProductStockSummary
WHERE "Difference" < 0;
```

#### 2. Hàm dùng chung `InventoryReconciler.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;

namespace PosApi.Services;

public static class InventoryReconciler
{
    public static async Task ReconcileAsync(AppDbContext context, int? targetWarehouseId = null)
    {
        Warehouse? defaultWarehouse = null;
        if (targetWarehouseId.HasValue)
        {
            defaultWarehouse = await context.Warehouses.FindAsync(targetWarehouseId.Value);
        }

        defaultWarehouse ??= await context.Warehouses.FirstOrDefaultAsync(w => w.IsDefault && w.IsActive)
            ?? await context.Warehouses.FirstOrDefaultAsync(w => w.IsActive);

        if (defaultWarehouse == null) return;

        var products = await context.Products.Where(p => p.IsActive).ToListAsync();

        foreach (var p in products)
        {
            var totalWhStock = await context.WarehouseStocks
                .Where(ws => ws.ProductId == p.Id)
                .SumAsync(ws => (decimal?)ws.Stock) ?? 0m;

            var diff = Math.Round(p.CurrentStock - totalWhStock, 3);
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
                    whStock.Stock = Math.Round(whStock.Stock + diff, 3);
                    whStock.UpdatedAt = DateTime.UtcNow;
                }

                var refId = "Q14-RECONCILE-" + p.Id;
                var existingLedger = await context.StockLedger.AnyAsync(l => l.RefId == refId);
                if (!existingLedger)
                {
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
            }
        }

        await context.SaveChangesAsync();
    }

    public static async Task RollbackReconciliationAsync(AppDbContext context)
    {
        var entries = await context.StockLedger
            .Where(l => l.RefId != null && l.RefId.StartsWith("Q14-RECONCILE-") && l.Note == "Cân bằng số dư đầu kỳ (Q14)")
            .ToListAsync();

        foreach (var entry in entries)
        {
            if (entry.WarehouseId.HasValue)
            {
                var whStock = await context.WarehouseStocks
                    .FirstOrDefaultAsync(ws => ws.WarehouseId == entry.WarehouseId.Value && ws.ProductId == entry.ProductId);
                if (whStock != null)
                {
                    whStock.Stock = Math.Round(whStock.Stock - entry.Qty, 3);
                    whStock.UpdatedAt = DateTime.UtcNow;
                    if (whStock.Stock <= 0m)
                    {
                        context.WarehouseStocks.Remove(whStock);
                    }
                }
            }
            context.StockLedger.Remove(entry);
        }

        await context.SaveChangesAsync();
    }
}
```

#### 3. Kiểm tra chặn trong chốt phiếu (`StockInService.cs`)
```csharp
// Q14 Invariant Check
var allProductWhStocks = await _db.WarehouseStocks
    .Where(s => productIds.Contains(s.ProductId))
    .GroupBy(s => s.ProductId)
    .Select(g => new { ProductId = g.Key, TotalStock = g.Sum(s => s.Stock) })
    .ToDictionaryAsync(x => x.ProductId, x => x.TotalStock);

foreach (var prod in products)
{
    var totalWh = allProductWhStocks.TryGetValue(prod.Id, out var ts) ? ts : 0m;
    if (prod.CurrentStock != totalWh)
    {
        throw new BusinessRuleException($"Sản phẩm '{prod.Name}' đang lệch bất biến tồn kho (CurrentStock: {prod.CurrentStock} != Tổng kho: {totalWh}). Vui lòng chạy công cụ cân bằng tồn kho (Q14) trước khi chốt phiếu nhập.");
    }
}
```

---

### B. Q13: Giá bình quân gia quyền (Weighted Average Cost)

#### 1. Logic tính toán trong `ConfirmStockInEntitiesAsync`
```csharp
var oldStock = product.CurrentStock;
var oldCost = product.CostPrice;
decimal newCost;
if (oldStock <= 0m)
{
    newCost = itemReq.UnitCost;
}
else
{
    newCost = Math.Round((oldStock * oldCost + itemReq.Qty * itemReq.UnitCost) / (oldStock + itemReq.Qty), 4);
}
product.CostPrice = newCost;

stockInItems.Add(new StockInItem
{
    StockIn = stockIn,
    ProductId = product.Id,
    Qty = itemReq.Qty,
    UnitCost = itemReq.UnitCost,
    Total = lineTotal,
    CostBefore = oldCost,
    CostAfter = newCost
});
```

#### 2. Giải đáp các câu hỏi kiểm tra (Section B.4):
- **`StockInItem.UnitCost` đã gồm VAT hay chưa?**  
  *Trả lời:* `UnitCost` trong `StockInItem` là giá vốn/giá nhập **chưa bao gồm VAT** (Net unit cost) được ghi nhận từ hóa đơn nhà cung cấp (`StockInService.cs:355`).
- **Đơn vị tính/quy đổi có ảnh hưởng không?**  
  *Trả lời:* Không, vì số lượng nhập `Qty` đã được quy đổi chuẩn về đơn vị gốc (Base unit) qua conversion rate trước khi ghi nhận vào `StockInItem`.
- **`OrderItem.CostPrice` có phải giá chốt tại thời điểm bán không?**  
  *Trả lời:* Trong thiết kế hiện tại, `OrderItem` không lưu trữ tĩnh `CostPrice` tại thời điểm bán; thay vào đó, báo cáo lợi nhuận gộp (`ReportService.GetAdvancedDashboardAsync`) tính toán COGS dựa trên giá vốn hiện tại `Product.CostPrice` tại thời điểm báo cáo (`ReportService.cs:176`).

---

### C. Bằng chứng kiểm định từ vòng trước

#### 1. Test song song `Parallel_StockIn_RealCommit_20Iterations` (kèm assert kho và ledger, lệnh trx)
```ps1
dotnet test pos-api/tests/PosApi.Tests/PosApi.Tests.csproj --filter Parallel_StockIn_RealCommit_20Iterations --logger "trx;LogFileName=par.trx"
```
```
Results File: E:\DigiSME_AI_Project\pos-api\tests\PosApi.Tests\TestResults\par.trx
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: < 1 ms - PosApi.Tests.dll (net8.0)
```
*(Đã bổ sung assert `WarehouseStock == 20` và `StockLedger count == 20` trong test).*

#### 2. Test Idempotency với cùng một Key cho 2 payload khác nhau
```ps1
dotnet test pos-api/tests/PosApi.Tests/PosApi.Tests.csproj --filter Idempotency_SameKey_DifferentPayload_ThrowsConflict
```
```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: < 1 ms - PosApi.Tests.dll (net8.0)
```
*(Giải thích lần trước báo lỗi `Failed: 1`: Do khi đó test chưa tạo bản ghi `WarehouseStock` tương ứng cho sản phẩm test trước khi gọi service, dẫn đến bị chặn bởi quy tắc bất biến Q14 mới bổ sung).*

#### 3. Phạm vi git diff so với `origin/main`
- **Merge-base:** `c712ec75aa16cd6e24b2f269bd2c9286ebb5f8ee`
- **Danh sách file thay đổi nhóm theo phân loại:**
  - **Thuộc B1 (Nhập kho đa kho & trạng thái):**
    - `pos-api/src/PosApi/Controllers/StockInsController.cs`
    - `pos-api/src/PosApi/Services/StockInService.cs`
    - `pos-api/src/PosApi/Services/InventoryReconciler.cs`
    - `pos-api/src/PosApi/Migrations/20261112162008_AddStockInWarehouseAndStatus.cs` (và Designer)
    - `pos-api/src/PosApi/Migrations/20261113155155_AddIsBackfilledToStockIns.cs` (và Designer)
    - `pos-api/src/PosApi/Migrations/20261114052118_AddQ14ReconciliationAndQ13Cost.cs` (và Designer)
    - `pos-api/tests/PosApi.Tests/StockInServiceTests.cs`
  - **Thuộc nhánh khác (B5/B7 — Ca làm việc, Transfer & Đơn vị tính):**
    - `pos-api/src/PosApi/Migrations/20261110160523_AddShiftDenominationsAndTransfer.cs` (và Designer)
    - `pos-api/src/PosApi/Controllers/ShiftsController.cs`, `ShiftsService.cs`, `OrdersController.cs`, v.v.
  - **Công cụ & Seed dữ liệu:**
    - `tools/seed/`, `tools/gap/`, `tools/q14/preview-mismatch.sql`

#### 4. Kết quả `dotnet test` toàn bộ
```ps1
dotnet test pos-api/tests/PosApi.Tests/PosApi.Tests.csproj
```
```
Test run for E:\DigiSME_AI_Project\pos-api\tests\PosApi.Tests\bin\Debug\net8.0\PosApi.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   161, Skipped:     0, Total:   161, Duration: 11 s - PosApi.Tests.dll (net8.0)
```

---

### D. Thiết kế Q12 & Cập nhật Open Questions
- Đã lập tài liệu thiết kế chi tiết thuế đầu vào phiếu nhập thủ công tại `docs/analysis/q12-input-vat-design.md`.
- Đã cập nhật trạng thái Q12 trong `docs/analysis/group8-open-questions.md` thành: *“Đã thiết kế, chờ kế toán xác nhận”*.
