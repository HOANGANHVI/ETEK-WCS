using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRcsFleetSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rcs_amr_snapshots",
                columns: table => new
                {
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RobotCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RobotDir = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RobotIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Battery = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PosX = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PosY = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MapCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Speed = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ExclType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Stop = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PodCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PodDir = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PathJson = table.Column<string>(type: "text", nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rcs_amr_snapshots", x => new { x.Source, x.RobotCode });
                });

            migrationBuilder.CreateTable(
                name: "rcs_fleet_state",
                columns: table => new
                {
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptOk = table.Column<bool>(type: "boolean", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastAmrCount = table.Column<int>(type: "integer", nullable: false),
                    LastLatencyMs = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rcs_fleet_state", x => x.Source);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rcs_amr_snapshots_Source_LastSeenAt",
                table: "rcs_amr_snapshots",
                columns: new[] { "Source", "LastSeenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rcs_amr_snapshots");

            migrationBuilder.DropTable(
                name: "rcs_fleet_state");
        }
    }
}
