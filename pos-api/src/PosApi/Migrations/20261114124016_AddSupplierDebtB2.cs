using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierDebtB2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "StockIns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "SupplierDebts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockInId",
                table: "SupplierDebts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "StockIns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "StockIns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierDebts_StockInId",
                table: "SupplierDebts",
                column: "StockInId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierDebts_StockIns_StockInId",
                table: "SupplierDebts",
                column: "StockInId",
                principalTable: "StockIns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplierDebts_StockIns_StockInId",
                table: "SupplierDebts");

            migrationBuilder.DropIndex(
                name: "IX_SupplierDebts_StockInId",
                table: "SupplierDebts");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "SupplierDebts");

            migrationBuilder.DropColumn(
                name: "StockInId",
                table: "SupplierDebts");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "StockIns");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "StockIns");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "StockIns");
        }
    }
}
