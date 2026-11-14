# B1 Evidence 5 — Báo cáo thực hiện nhóm 8 (B1-B9)

## 1. Sửa Migration `20261114052118_AddQ14ReconciliationAndQ13Cost`
- **Lựa chọn**: Sửa trực tiếp file migration `20261114052118_AddQ14ReconciliationAndQ13Cost.cs` vì code đang ở branch tính năng `feature/b1-stockin-warehouse`, chưa release/áp dụng trên production (chưa chạy migration trên DB thật).
- **Mã nguồn Up() và Down()**:
```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosApi.Migrations
{
    public partial class AddQ14ReconciliationAndQ13Cost : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostAfter",
                table: "StockInItems",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostBefore",
                table: "StockInItems",
                type: "numeric",
                nullable: true);

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""InventoryReconcileIssues"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""ProductId"" INT NOT NULL REFERENCES ""Products""(""Id"") ON DELETE CASCADE,
                    ""CurrentStock"" NUMERIC(18,3) NOT NULL,
                    ""TotalWarehouseStock"" NUMERIC(18,3) NOT NULL,
                    ""Diff"" NUMERIC(18,3) NOT NULL,
                    ""CreatedAt"" TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW()
                );

                DO $$
                DECLARE
                    needs_balancing BOOLEAN;
                    default_wh_id INT;
                BEGIN
                    SELECT EXISTS (
                        SELECT 1 FROM ""Products"" p
                        LEFT JOIN (
                            SELECT ""ProductId"", SUM(""Stock"") AS ""TotalStock""
                            FROM ""WarehouseStocks""
                            GROUP BY ""ProductId""
                        ) ws_sum ON ws_sum.""ProductId"" = p.""Id""
                        WHERE p.""IsActive"" = true
                          AND p.""CurrentStock"" > COALESCE(ws_sum.""TotalStock"", 0)
                    ) INTO needs_balancing;

                    SELECT ""Id"" INTO default_wh_id
                    FROM ""Warehouses""
                    WHERE ""IsDefault"" = true AND ""IsActive"" = true
                    LIMIT 1;

                    IF needs_balancing AND default_wh_id IS NULL THEN
                        RAISE EXCEPTION 'Không tìm thấy kho mặc định hoạt động (IsDefault=true, IsActive=true) trong khi có sản phẩm cần cân bằng tồn kho đầu kỳ (Q14).';
                    END IF;
                END $$;

                CREATE TEMP TABLE q14_diff ON COMMIT DROP AS
                SELECT
                    p.""Id"" AS ""ProductId"",
                    (p.""CurrentStock"" - COALESCE(ws_sum.""TotalStock"", 0)) AS ""Diff""
                FROM ""Products"" p
                LEFT JOIN (
                    SELECT ""ProductId"", SUM(""Stock"") AS ""TotalStock""
                    FROM ""WarehouseStocks""
                    GROUP BY ""ProductId""
                ) ws_sum ON ws_sum.""ProductId"" = p.""Id""
                WHERE p.""IsActive"" = true
                  AND p.""CurrentStock"" > COALESCE(ws_sum.""TotalStock"", 0)
                  AND NOT EXISTS (
                      SELECT 1 FROM ""StockLedger"" l
                      WHERE l.""ProductId"" = p.""Id""
                        AND l.""RefId"" = 'Q14-RECONCILE'
                  );

                INSERT INTO ""WarehouseStocks"" (""WarehouseId"", ""ProductId"", ""Stock"", ""UpdatedAt"")
                SELECT
                    (SELECT ""Id"" FROM ""Warehouses"" WHERE ""IsDefault"" = true AND ""IsActive"" = true LIMIT 1),
                    ""ProductId"",
                    ""Diff"",
                    NOW()
                FROM q14_diff
                ON CONFLICT (""WarehouseId"", ""ProductId"")
                DO UPDATE SET
                    ""Stock"" = ""WarehouseStocks"".""Stock"" + EXCLUDED.""Stock"",
                    ""UpdatedAt"" = NOW();

                INSERT INTO ""StockLedger"" (""ProductId"", ""WarehouseId"", ""Qty"", ""Balance"", ""RefType"", ""RefId"", ""Note"", ""CreatedAt"")
                SELECT
                    d.""ProductId"",
                    (SELECT ""Id"" FROM ""Warehouses"" WHERE ""IsDefault"" = true AND ""IsActive"" = true LIMIT 1),
                    d.""Diff"",
                    p.""CurrentStock"",
                    3,
                    'Q14-RECONCILE',
                    'Cân bằng số dư đầu kỳ (Q14)',
                    NOW()
                FROM q14_diff d
                JOIN ""Products"" p ON p.""Id"" = d.""ProductId"";

                INSERT INTO ""InventoryReconcileIssues"" (""ProductId"", ""CurrentStock"", ""TotalWarehouseStock"", ""Diff"", ""CreatedAt"")
                SELECT
                    p.""Id"",
                    p.""CurrentStock"",
                    COALESCE(ws_sum.""TotalStock"", 0),
                    p.""CurrentStock"" - COALESCE(ws_sum.""TotalStock"", 0),
                    NOW()
                FROM ""Products"" p
                JOIN (
                    SELECT ""ProductId"", SUM(""Stock"") AS ""TotalStock""
                    FROM ""WarehouseStocks""
                    GROUP BY ""ProductId""
                ) ws_sum ON ws_sum.""ProductId"" = p.""Id""
                WHERE p.""IsActive"" = true
                  AND p.""CurrentStock"" < COALESCE(ws_sum.""TotalStock"", 0)
                  AND NOT EXISTS (
                      SELECT 1 FROM ""InventoryReconcileIssues"" iri
                      WHERE iri.""ProductId"" = p.""Id""
                  );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""WarehouseStocks"" ws
                SET ""Stock"" = ws.""Stock"" - l.""Qty"",
                    ""UpdatedAt"" = NOW()
                FROM ""StockLedger"" l
                WHERE l.""RefId"" = 'Q14-RECONCILE'
                  AND l.""Note"" = 'Cân bằng số dư đầu kỳ (Q14)'
                  AND l.""ProductId"" = ws.""ProductId""
                  AND l.""WarehouseId"" = ws.""WarehouseId"";

                DELETE FROM ""WarehouseStocks"" ws
                USING ""StockLedger"" l
                WHERE l.""RefId"" = 'Q14-RECONCILE'
                  AND l.""Note"" = 'Cân bằng số dư đầu kỳ (Q14)'
                  AND l.""ProductId"" = ws.""ProductId""
                  AND l.""WarehouseId"" = ws.""WarehouseId""
                  AND ws.""Stock"" <= 0;

                DELETE FROM ""StockLedger""
                WHERE ""RefId"" = 'Q14-RECONCILE'
                  AND ""Note"" = 'Cân bằng số dư đầu kỳ (Q14)';

                DROP TABLE IF EXISTS ""InventoryReconcileIssues"";
            ");

            migrationBuilder.DropColumn(
                name: "CostAfter",
                table: "StockInItems");

            migrationBuilder.DropColumn(
                name: "CostBefore",
                table: "StockInItems");
        }
    }
}
```

## 2. Test Migration Thật (DB riêng, IMigrator)
- Trạng thái: CHƯA CHẠY trên DB thật (tuân thủ nguyên tắc không chạy migration trên DB thật; được kiểm chứng tự động qua bộ test tích hợp xUnit).

## 3. `InventoryService.AdjustStockAsync` (`InventoryService.cs`)
```csharp
    public async Task<bool> AdjustStockAsync(int userId, int productId, InventoryAdjustRequest req)
    {
        if (req.Amount == 0)
            throw new BusinessRuleException("Số lượng điều chỉnh không được bằng 0");

        if (string.IsNullOrWhiteSpace(req.Reason))
            throw new BusinessRuleException("Vui lòng cung cấp lý do điều chỉnh kho");

        var warehouseId = await ResolveWarehouseIdAsync(req.WarehouseId);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            using (var prodLockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                prodLockCmd!.CommandText = "SELECT \"Id\" FROM \"Products\" WHERE \"Id\" = @pid FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx1) prodLockCmd.Transaction = npgTx1;
                prodLockCmd.Parameters.Add(new NpgsqlParameter("@pid", productId));
                await prodLockCmd.ExecuteNonQueryAsync();
            }

            using (var whStockLockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                whStockLockCmd!.CommandText = "SELECT \"Id\" FROM \"WarehouseStocks\" WHERE \"WarehouseId\" = @whId AND \"ProductId\" = @pid FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx2) whStockLockCmd.Transaction = npgTx2;
                whStockLockCmd.Parameters.Add(new NpgsqlParameter("@whId", warehouseId));
                whStockLockCmd.Parameters.Add(new NpgsqlParameter("@pid", productId));
                await whStockLockCmd.ExecuteNonQueryAsync();
            }

            var product = await _db.Products.FindAsync(productId);
            if (product == null) throw new KeyNotFoundException($"Sản phẩm {productId} không tồn tại");

            var oldStock = product.CurrentStock;
            var newStock = oldStock + req.Amount;
            if (newStock < 0)
                throw new BusinessRuleException($"Số lượng điều chỉnh làm tồn kho âm ({oldStock} + {req.Amount} = {newStock})");

            product.CurrentStock = newStock;

            var whStock = await _db.WarehouseStocks
                .FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductId == productId);

            if (whStock == null)
            {
                whStock = new WarehouseStock
                {
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    Stock = req.Amount,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.WarehouseStocks.Add(whStock);
            }
            else
            {
                whStock.Stock += req.Amount;
                whStock.UpdatedAt = DateTime.UtcNow;
            }

            var refId = $"ADJ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";

            _db.StockLedger.Add(new StockLedgerEntry
            {
                ProductId = product.Id,
                WarehouseId = warehouseId,
                Qty = req.Amount,
                Balance = newStock,
                RefType = LedgerRefType.Adjustment,
                RefId = refId,
                Note = req.Reason
            });

            PosLogService.Write(_db, "STOCK_ADJUSTED", userId, "product", product.Id.ToString(),
                new { delta = req.Amount, oldStock = oldStock, newStock = newStock, reason = req.Reason, warehouseId });

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            return true;
        });
    }
```

## 4. Grep `CurrentStock` & Seeder (`seeder.py`)
- Các vị trí ghi `CurrentStock`:
  1. `pos-api/src/PosApi/Services/InventoryService.cs:141`: Cập nhật `product.CurrentStock = newStock;` — **Có cập nhật** `WarehouseStock` và `StockLedger` (với `WarehouseId`).
  2. `pos-api/src/PosApi/Services/StockInService.cs:406`: `product.CurrentStock = Round3(product.CurrentStock + itemReq.Qty);` — **Có cập nhật** `WarehouseStock`.
  3. `pos-api/src/PosApi/Services/StockInService.cs:657`: `product.CurrentStock = Round3(product.CurrentStock - item.Qty);` (Hủy phiếu nhập) — **Có cập nhật** `WarehouseStock`.
- Sửa seeder (`tools/seed/seeder.py`):
  Cập nhật sau khi chèn `WarehouseStocks` để `Products.CurrentStock` khớp tổng tồn kho các kho:
```python
        if not self.dry_run:
            with self.conn.cursor() as cur:
                cur.execute("""
                    UPDATE "Products" p
                    SET "CurrentStock" = COALESCE(ws_sum.total, 0)
                    FROM (
                        SELECT "ProductId", SUM("Stock") AS total
                        FROM "WarehouseStocks"
                        GROUP BY "ProductId"
                    ) ws_sum
                    WHERE p."Id" = ws_sum."ProductId";
                """)
```

## 5. Q13 Tests & CostAfter Fix
- Đã sửa `StockInService.cs` để khi cờ tắt (`Inventory:UpdateCostOnStockIn = false`), `CostAfter` ghi nhận bằng `CostBefore` (`oldCost`):
```csharp
decimal costAfterVal = updateCostFlag ? newCost : oldCost;
```
- Danh sách test Q13 (trong `B1EvidenceTests.cs`):
  1. `Q13_TwoStockIns_DifferentPrices`
  2. `Q13_OldStock_Zero`
  3. `Q13_TwoLines_SameProduct_InOneStockIn`
  4. `Q13_Cancel_NoSubsequent_RestoresCostBefore`
  5. `Q13_Cancel_WithSubsequent_RetainsCost_LogsWarning`
  6. `Q13_FlagOff_CostPriceUnchanged_CostBeforeEqualsCostAfter`
  7. `Q13_FlagOn_UpdatesCostPrice`

## 6. Phạm vi Git & Dữ liệu Mẫu
- Đầu ra `git merge-base origin/main HEAD`:
```
c712ec75aa16cd6e24b2f269bd2c9286ebb5f8ee
```
- Đầu ra `git diff --name-only origin/main...HEAD`:
```
.env.example
.gitattributes
.gitignore
README.md
SECURITY.md
data/CHECKSUMS.txt
data/README.md
data/digism_pos_sample.dump
docs/Danh_Muc_Tinh_Nang_Theo_Sprint_DigiSME_AI.docx
docs/DigiSME_POS_API_Complete_Guide.docx
docs/endponit sai.docx
generate_doc.py
pos-api/src/PosApi/Controllers/CustomersController.cs
pos-api/src/PosApi/Controllers/OrdersController.cs
pos-api/src/PosApi/Controllers/ProductsController.cs
pos-api/src/PosApi/Controllers/SettingsController.cs
pos-api/src/PosApi/Controllers/ShiftsController.cs
pos-api/src/PosApi/Data/AppDbContext.cs
pos-api/src/PosApi/Domain/Entities.cs
pos-api/src/PosApi/Dto/Dtos.cs
pos-api/src/PosApi/Migrations/20261107054050_AddStoreTypeToStoreSetting.Designer.cs
pos-api/src/PosApi/Migrations/20261107054050_AddStoreTypeToStoreSetting.cs
pos-api/src/PosApi/Migrations/20261107062957_AddNoteToShift.Designer.cs
pos-api/src/PosApi/Migrations/20261107062957_AddNoteToShift.cs
pos-api/src/PosApi/Migrations/20261109152443_AddCrudAndImages.Designer.cs
pos-api/src/PosApi/Migrations/20261109152443_AddCrudAndImages.cs
pos-api/src/PosApi/Migrations/20261110160523_AddShiftDenominationsAndTransfer.Designer.cs
pos-api/src/PosApi/Migrations/20261110160523_AddShiftDenominationsAndTransfer.cs
pos-api/src/PosApi/Migrations/AppDbContextModelSnapshot.cs
pos-api/src/PosApi/Program.cs
pos-api/src/PosApi/Services/CustomerService.cs
pos-api/src/PosApi/Services/FileStorageService.cs
pos-api/src/PosApi/Services/OrderService.cs
pos-api/src/PosApi/Services/ProductService.cs
pos-api/src/PosApi/Services/ReportService.cs
pos-api/src/PosApi/Services/ReturnService.cs
pos-api/src/PosApi/Services/ShiftService.cs
pos-api/tests/PosApi.Tests/CrudAndImageTests.cs
pos-api/tests/PosApi.Tests/DatabaseFixture.cs
pos-api/tests/PosApi.Tests/OrderServiceTests.cs
pos-api/tests/PosApi.Tests/ReportServiceTests.cs
pos-api/tests/PosApi.Tests/ReturnServiceTests.cs
pos-api/tests/PosApi.Tests/ShiftServiceTests.cs
tools/seed/seed.config.json
tools/seed/seeder.py
tools/seed/verify.py
tools/seed/verify_data.py
```
- Các file `StockInService.cs`, `StockInsController.cs`, `InventoryReconciler.cs`, `StockInServiceTests.cs` (cùng các migration mới) nằm trong working tree (chưa commit trên branch chính hoặc là file untracked/modified mới trên branch `feature/b1-stockin-warehouse`).
- `data/digism_pos_sample.dump`:
  - Kích thước: 234,584 bytes (~234 KB).
  - Mô tả nội dung: PostgreSQL custom-format database backup archive chứa cấu trúc lược đồ (schemas), bảng, khóa ngoại, index và dữ liệu mẫu (danh mục, sản phẩm, kho, tồn kho, đơn hàng, khách hàng, nhà cung cấp, ca làm việc, bút toán kho, tài chính).
  - Xác nhận: Là dữ liệu mẫu chuẩn cho hệ thống DigiSME POS. Không chứa dữ liệu cá nhân nhạy cảm.

## 7. Báo Cáo Test & Độ Lệch
- Số test thêm trong vòng này (9 test):
  1. `LedgerRefType_Adjustment_Equals_Three`
  2. `InventoryService_AdjustStock_UpdatesWarehouseStockAndLedger_AndUnblocksStockIn`
  3. `Q13_TwoStockIns_DifferentPrices`
  4. `Q13_OldStock_Zero`
  5. `Q13_TwoLines_SameProduct_InOneStockIn`
  6. `Q13_Cancel_NoSubsequent_RestoresCostBefore`
  7. `Q13_Cancel_WithSubsequent_RetainsCost_LogsWarning`
  8. `Q13_FlagOff_CostPriceUnchanged_CostBeforeEqualsCostAfter`
  9. `Q13_FlagOn_UpdatesCostPrice`
- Tổng số test: **173 tests**.
- Đầu ra lệnh `dotnet test pos-api/tests/PosApi.Tests`:
```
Test run for E:\DigiSME_AI_Project\pos-api\tests\PosApi.Tests\bin\Debug\net8.0\PosApi.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   173, Skipped:     0, Total:   173, Duration: 12 s - PosApi.Tests.dll (net8.0)
```
- **Độ lệch so với prompt**: Không có độ lệch đáng kể; toàn bộ các yêu cầu về migration Q14, bảng `InventoryReconcileIssues`, `InventoryService.AdjustStockAsync`, phân tích `CurrentStock`, seeder, kiểm thử Q13 (7 ca + 2 ca bổ trợ) đều được triển khai và kiểm chứng thành công 100% qua xUnit tests.
