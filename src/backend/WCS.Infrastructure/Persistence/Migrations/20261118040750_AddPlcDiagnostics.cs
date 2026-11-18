using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlcDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiagCode",
                table: "plc_comm_log",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "plc_comm_log",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plc_exchange_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlcId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Operation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Command = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Device = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    StartAddress = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    EndCode = table.Column<int>(type: "integer", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RequestHex = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ResponseHex = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    IsAmbiguous = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plc_exchange_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_plc_comm_log_PlcId_Stage_OccurredAt",
                table: "plc_comm_log",
                columns: new[] { "PlcId", "Stage", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_plc_exchange_log_PlcId_OccurredAt",
                table: "plc_exchange_log",
                columns: new[] { "PlcId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plc_exchange_log");

            migrationBuilder.DropIndex(
                name: "IX_plc_comm_log_PlcId_Stage_OccurredAt",
                table: "plc_comm_log");

            migrationBuilder.DropColumn(
                name: "DiagCode",
                table: "plc_comm_log");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "plc_comm_log");
        }
    }
}
