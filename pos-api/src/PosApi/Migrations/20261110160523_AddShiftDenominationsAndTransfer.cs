using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosApi.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftDenominationsAndTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CountedTransfer",
                table: "Shifts",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DenominationsJson",
                table: "Shifts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransferDiff",
                table: "Shifts",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransferSales",
                table: "Shifts",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountedTransfer",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "DenominationsJson",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "TransferDiff",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "TransferSales",
                table: "Shifts");
        }
    }
}
