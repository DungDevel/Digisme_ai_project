using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosApi.Migrations
{
    /// <inheritdoc />
    public partial class AddStockInWarehouseAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreatedBy",
                table: "StockIns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "StockIns",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "StockIns",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""StockIns""
                SET ""WarehouseId"" = COALESCE(
                    (SELECT ""Id"" FROM ""Warehouses"" WHERE ""IsDefault"" = true LIMIT 1),
                    (SELECT ""Id"" FROM ""Warehouses"" ORDER BY ""Id"" LIMIT 1),
                    1
                )
                WHERE ""WarehouseId"" IS NULL;
            ");

            migrationBuilder.AlterColumn<int>(
                name: "WarehouseId",
                table: "StockIns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockIns_WarehouseId",
                table: "StockIns",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockIns_Warehouses_WarehouseId",
                table: "StockIns",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockIns_Warehouses_WarehouseId",
                table: "StockIns");

            migrationBuilder.DropIndex(
                name: "IX_StockIns_WarehouseId",
                table: "StockIns");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "StockIns");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "StockIns");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "StockIns");
        }
    }
}
