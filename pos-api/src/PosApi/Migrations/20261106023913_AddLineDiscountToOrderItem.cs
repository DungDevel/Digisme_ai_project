using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosApi.Migrations
{
    /// <inheritdoc />
    public partial class AddLineDiscountToOrderItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LineDiscount",
                table: "OrderItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Vat",
                table: "OrderItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LineDiscount",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "Vat",
                table: "OrderItems");
        }
    }
}
