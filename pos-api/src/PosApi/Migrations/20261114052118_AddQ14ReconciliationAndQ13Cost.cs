using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosApi.Migrations
{
    /// <inheritdoc />
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
                -- Create InventoryReconcileIssues table if not exists
                CREATE TABLE IF NOT EXISTS ""InventoryReconcileIssues"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""ProductId"" INT NOT NULL REFERENCES ""Products""(""Id"") ON DELETE CASCADE,
                    ""CurrentStock"" NUMERIC(18,3) NOT NULL,
                    ""TotalWarehouseStock"" NUMERIC(18,3) NOT NULL,
                    ""Diff"" NUMERIC(18,3) NOT NULL,
                    ""CreatedAt"" TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW()
                );

                -- Check if any active products need balancing (CurrentStock > TotalWarehouseStock)
                -- and ensure an active default warehouse exists.
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

                -- Create temp table q14_diff prior to any changes
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

                -- 1. Upsert WarehouseStocks for default warehouse using q14_diff
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

                -- 2. Insert StockLedger using q14_diff
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

                -- 3. Record products where CurrentStock < TotalWarehouseStock into InventoryReconcileIssues
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
                -- 1. Reverse WarehouseStocks using StockLedger where RefId = 'Q14-RECONCILE'
                UPDATE ""WarehouseStocks"" ws
                SET ""Stock"" = ws.""Stock"" - l.""Qty"",
                    ""UpdatedAt"" = NOW()
                FROM ""StockLedger"" l
                WHERE l.""RefId"" = 'Q14-RECONCILE'
                  AND l.""Note"" = 'Cân bằng số dư đầu kỳ (Q14)'
                  AND l.""ProductId"" = ws.""ProductId""
                  AND l.""WarehouseId"" = ws.""WarehouseId"";

                -- 2. Delete WarehouseStocks created by migration (rows with Q14 ledger and resulting stock <= 0)
                DELETE FROM ""WarehouseStocks"" ws
                USING ""StockLedger"" l
                WHERE l.""RefId"" = 'Q14-RECONCILE'
                  AND l.""Note"" = 'Cân bằng số dư đầu kỳ (Q14)'
                  AND l.""ProductId"" = ws.""ProductId""
                  AND l.""WarehouseId"" = ws.""WarehouseId""
                  AND ws.""Stock"" <= 0;

                -- 3. Delete StockLedger entries for Q14
                DELETE FROM ""StockLedger""
                WHERE ""RefId"" = 'Q14-RECONCILE'
                  AND ""Note"" = 'Cân bằng số dư đầu kỳ (Q14)';

                -- 4. Drop InventoryReconcileIssues table
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
