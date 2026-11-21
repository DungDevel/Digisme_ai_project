using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosApi.Migrations
{
    /// <inheritdoc />
    public partial class AddStockTakeApprovalAndThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOwner",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE \"Users\" SET \"IsOwner\" = true WHERE \"Username\" = 'admin';");

            migrationBuilder.AddColumn<decimal>(
                name: "StockTakeAutoLimit",
                table: "StoreSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StockTakeManagerLimit",
                table: "StoreSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalLevel",
                table: "StockTakes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "StockTakes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfirmedBy",
                table: "StockTakes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDeltaValue",
                table: "StockTakes",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOwner",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StockTakeAutoLimit",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "StockTakeManagerLimit",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ApprovalLevel",
                table: "StockTakes");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "StockTakes");

            migrationBuilder.DropColumn(
                name: "ConfirmedBy",
                table: "StockTakes");

            migrationBuilder.DropColumn(
                name: "TotalDeltaValue",
                table: "StockTakes");
        }
    }
}
