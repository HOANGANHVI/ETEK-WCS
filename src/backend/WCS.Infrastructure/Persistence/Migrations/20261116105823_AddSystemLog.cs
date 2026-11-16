using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailsJson",
                table: "plc_comm_log",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "system_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Device = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    EventCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Exception = table.Column<string>(type: "text", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    Host = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_log_Category_OccurredAt",
                table: "system_log",
                columns: new[] { "Category", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_system_log_Device_OccurredAt",
                table: "system_log",
                columns: new[] { "Device", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_system_log_EventCode",
                table: "system_log",
                column: "EventCode");

            migrationBuilder.CreateIndex(
                name: "IX_system_log_ExternalId",
                table: "system_log",
                column: "ExternalId",
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_system_log_Level_OccurredAt",
                table: "system_log",
                columns: new[] { "Level", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_system_log_OccurredAt",
                table: "system_log",
                column: "OccurredAt");

            // CR-007: read-only unified view. No data is copied: system_log, plc_comm_log and audit_log stay the single source of truth for their own events.
            migrationBuilder.Sql(@"
CREATE VIEW v_log_entries AS
SELECT 'sys:' || s.""Id""::text AS ""EntryId"", s.""OccurredAt"", s.""Category"", s.""Level"",
       CASE s.""Level"" WHEN 'Critical' THEN 4 WHEN 'Error' THEN 3 WHEN 'Warning' THEN 2 ELSE 1 END AS ""LevelRank"",
       s.""Source"", s.""Device"", s.""EventCode"" AS ""Event"", s.""Message""
  FROM system_log s
UNION ALL
SELECT 'plc:' || p.""Id""::text, p.""OccurredAt"", 'PLC',
       CASE p.""Level"" WHEN 'Error' THEN 'Error' WHEN 'Warning' THEN 'Warning' ELSE 'Information' END,
       CASE p.""Level"" WHEN 'Error' THEN 3 WHEN 'Warning' THEN 2 ELSE 1 END,
       'PLC.Runtime', p.""PlcId"", p.""Event"", p.""Message""
  FROM plc_comm_log p
UNION ALL
SELECT 'aud:' || a.""Id""::text, a.""OccurredAt"", 'SECURITY_AUDIT',
       CASE a.""Result"" WHEN 'Success' THEN 'Information' ELSE 'Warning' END,
       CASE a.""Result"" WHEN 'Success' THEN 1 ELSE 2 END,
       'Audit', a.""Actor"", a.""Action"", COALESCE(a.""Detail"", a.""Action"" || ' ' || a.""Result"")
  FROM audit_log a;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_log_entries;");

            migrationBuilder.DropTable(
                name: "system_log");

            migrationBuilder.DropColumn(
                name: "DetailsJson",
                table: "plc_comm_log");
        }
    }
}
