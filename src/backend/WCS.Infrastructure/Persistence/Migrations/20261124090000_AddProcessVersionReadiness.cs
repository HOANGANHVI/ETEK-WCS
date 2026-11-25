using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// CR-035 (D-11): Operational Readiness axis on business_process_versions. Columns are added only. EVERY existing row becomes 'NotReady': nothing proves that an existing
    /// Published version is operationally ready, so Ready is never inferred from the publication state. An Admin sets Ready explicitly (with a reason, audited) before
    /// RouteEngine:EnforceReadiness is switched on. With the flag off (default) behaviour is unchanged. Down removes only the added columns.
    /// </summary>
    [DbContext(typeof(WcsDbContext))]
    [Migration("20261124090000_AddProcessVersionReadiness")]
    public partial class AddProcessVersionReadiness : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "Readiness", table: "business_process_versions", type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "NotReady");
            migrationBuilder.AddColumn<DateTimeOffset>(name: "ReadinessChangedAt", table: "business_process_versions", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ReadinessChangedBy", table: "business_process_versions", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "ReadinessReason", table: "business_process_versions", type: "character varying(500)", maxLength: 500, nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Readiness", table: "business_process_versions");
            migrationBuilder.DropColumn(name: "ReadinessChangedAt", table: "business_process_versions");
            migrationBuilder.DropColumn(name: "ReadinessChangedBy", table: "business_process_versions");
            migrationBuilder.DropColumn(name: "ReadinessReason", table: "business_process_versions");
        }
    }
}
