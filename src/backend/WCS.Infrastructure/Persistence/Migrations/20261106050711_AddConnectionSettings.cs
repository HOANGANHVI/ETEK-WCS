using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "connection_settings",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SettingsJson = table.Column<string>(type: "text", nullable: false),
                    ProtectedSecrets = table.Column<string>(type: "text", nullable: true),
                    LastTestAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTestOutcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LastTestMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LastTestMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastLatencyMs = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connection_settings", x => x.Kind);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agvs_RcsRobotCode",
                table: "agvs",
                column: "RcsRobotCode",
                unique: true,
                filter: "\"RcsRobotCode\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connection_settings");

            migrationBuilder.DropIndex(
                name: "IX_agvs_RcsRobotCode",
                table: "agvs");
        }
    }
}
