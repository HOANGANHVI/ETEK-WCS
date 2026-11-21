using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLegRobotCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignedAt",
                table: "transport_legs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RobotCode",
                table: "transport_legs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_legs_RobotCode",
                table: "transport_legs",
                column: "RobotCode");

            // CR-028: when the columns come back after a rollback, the AMR of each leg that was saved by Down is restored (the backup table is kept; nothing is deleted).
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF to_regclass('transport_leg_robot_backup') IS NOT NULL THEN
        UPDATE transport_legs l SET ""RobotCode"" = b.""RobotCode"", ""AssignedAt"" = b.""AssignedAt""
        FROM transport_leg_robot_backup b WHERE b.""LegId"" = l.""Id"";
    END IF;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CR-028 rollback: dropping the columns would lose which AMR carried each leg (history). The values are first COPIED to transport_leg_robot_backup (kept after the rollback, outside the EF model,
            // restored by Up if the migration is applied again). Nothing is deleted, no AGV is assigned.
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS transport_leg_robot_backup (
    ""LegId"" uuid PRIMARY KEY, ""RobotCode"" varchar(64) NOT NULL, ""AssignedAt"" timestamp with time zone NULL, ""SavedAt"" timestamp with time zone NOT NULL DEFAULT now());
INSERT INTO transport_leg_robot_backup (""LegId"", ""RobotCode"", ""AssignedAt"")
    SELECT ""Id"", ""RobotCode"", ""AssignedAt"" FROM transport_legs WHERE ""RobotCode"" IS NOT NULL
    ON CONFLICT (""LegId"") DO UPDATE SET ""RobotCode"" = EXCLUDED.""RobotCode"", ""AssignedAt"" = EXCLUDED.""AssignedAt"", ""SavedAt"" = now();");

            migrationBuilder.DropIndex(
                name: "IX_transport_legs_RobotCode",
                table: "transport_legs");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "transport_legs");

            migrationBuilder.DropColumn(
                name: "RobotCode",
                table: "transport_legs");
        }
    }
}
