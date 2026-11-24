using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PosApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCountersAndPosDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxCounters",
                table: "StoreSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxPosDevices",
                table: "StoreSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Counters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Counters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PosDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CounterId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosDevices_Counters_CounterId",
                        column: x => x.CounterId,
                        principalTable: "Counters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddColumn<int>(
                name: "CounterId",
                table: "Shifts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PosDeviceId",
                table: "Shifts",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
                INSERT INTO ""Counters"" (""Code"", ""Name"", ""IsDefault"", ""IsActive"", ""CreatedAt"")
                SELECT 'DEFAULT', 'Quầy mặc định', true, true, NOW()
                WHERE NOT EXISTS (SELECT 1 FROM ""Counters"" WHERE ""Code"" = 'DEFAULT');
            ");

            migrationBuilder.Sql(@"
                UPDATE ""Shifts""
                SET ""CounterId"" = (SELECT ""Id"" FROM ""Counters"" WHERE ""Code"" = 'DEFAULT' LIMIT 1)
                WHERE ""CounterId"" IS NULL;
            ");

            migrationBuilder.AlterColumn<int>(
                name: "CounterId",
                table: "Shifts",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_CounterId",
                table: "Shifts",
                column: "CounterId");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_PosDeviceId",
                table: "Shifts",
                column: "PosDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Counters_Code",
                table: "Counters",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Counters_IsDefault",
                table: "Counters",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_PosDevices_Code",
                table: "PosDevices",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosDevices_CounterId",
                table: "PosDevices",
                column: "CounterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_Counters_CounterId",
                table: "Shifts",
                column: "CounterId",
                principalTable: "Counters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_PosDevices_PosDeviceId",
                table: "Shifts",
                column: "PosDeviceId",
                principalTable: "PosDevices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_Counters_CounterId",
                table: "Shifts");

            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_PosDevices_PosDeviceId",
                table: "Shifts");

            migrationBuilder.DropTable(
                name: "PosDevices");

            migrationBuilder.DropTable(
                name: "Counters");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_CounterId",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_PosDeviceId",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "MaxCounters",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "MaxPosDevices",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "CounterId",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "PosDeviceId",
                table: "Shifts");
        }
    }
}
