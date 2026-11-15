using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

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

            migrationBuilder.CreateTable(
                name: "InventoryReconcileIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    CurrentStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    TotalWarehouseStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Diff = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReconcileIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryReconcileIssues_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReconcileIssues_ProductId",
                table: "InventoryReconcileIssues",
                column: "ProductId");

            migrationBuilder.Sql(@"
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
                  AND ""Note"" = 'Cân bằng số dư đầu kỳ (Q14)'
            ");

            migrationBuilder.DropTable(
                name: "InventoryReconcileIssues");

            migrationBuilder.DropColumn(
                name: "CostAfter",
                table: "StockInItems");

            migrationBuilder.DropColumn(
                name: "CostBefore",
                table: "StockInItems");
        }
    }
}
