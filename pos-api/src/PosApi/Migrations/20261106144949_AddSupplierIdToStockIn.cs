using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierIdToStockIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "StockIns",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockIns_SupplierId",
                table: "StockIns",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockIns_Suppliers_SupplierId",
                table: "StockIns",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockIns_Suppliers_SupplierId",
                table: "StockIns");

            migrationBuilder.DropIndex(
                name: "IX_StockIns_SupplierId",
                table: "StockIns");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "StockIns");
        }
    }
}
