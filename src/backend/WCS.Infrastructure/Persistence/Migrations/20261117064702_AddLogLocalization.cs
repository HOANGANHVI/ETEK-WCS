using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Bilingual log display: stores the stable message template of system events and exposes it, together with the event parameters
    /// (<c>DetailsJson</c>), through the unified log view. Additive: no data is rewritten; older rows simply have no template.
    /// </summary>
    public partial class AddLogLocalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MessageTemplate",
                table: "system_log",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql("DROP VIEW IF EXISTS v_log_entries;");
            migrationBuilder.Sql(@"
CREATE VIEW v_log_entries AS
SELECT 'sys:' || s.""Id""::text AS ""EntryId"", s.""OccurredAt"", s.""Category"", s.""Level"",
       CASE s.""Level"" WHEN 'Critical' THEN 4 WHEN 'Error' THEN 3 WHEN 'Warning' THEN 2 ELSE 1 END AS ""LevelRank"",
       s.""Source"", s.""Device"", s.""EventCode"" AS ""Event"", s.""Message"", s.""MessageTemplate"", s.""DetailsJson""
  FROM system_log s
UNION ALL
SELECT 'plc:' || p.""Id""::text, p.""OccurredAt"", 'PLC',
       CASE p.""Level"" WHEN 'Error' THEN 'Error' WHEN 'Warning' THEN 'Warning' ELSE 'Information' END,
       CASE p.""Level"" WHEN 'Error' THEN 3 WHEN 'Warning' THEN 2 ELSE 1 END,
       'PLC.Runtime', p.""PlcId"", p.""Event"", p.""Message"", NULL::character varying, p.""DetailsJson""
  FROM plc_comm_log p
UNION ALL
SELECT 'aud:' || a.""Id""::text, a.""OccurredAt"", 'SECURITY_AUDIT',
       CASE a.""Result"" WHEN 'Success' THEN 'Information' ELSE 'Warning' END,
       CASE a.""Result"" WHEN 'Success' THEN 1 ELSE 2 END,
       'Audit', a.""Actor"", a.""Action"", COALESCE(a.""Detail"", a.""Action"" || ' ' || a.""Result""), NULL::character varying, NULL::text
  FROM audit_log a;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_log_entries;");
            migrationBuilder.DropColumn(
                name: "MessageTemplate",
                table: "system_log");
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
    }
}
