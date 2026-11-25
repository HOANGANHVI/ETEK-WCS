using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessRequiresReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresReadiness",
                table: "business_processes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CR-039: the earlier version does not check device conditions at intake; dropping the flag would let such a process accept requests. Disable those processes first.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM business_processes p JOIN business_process_versions v ON v."ProcessId" = p."Id" WHERE p."RequiresReadiness" AND v."State" = 'Published') THEN
                        RAISE EXCEPTION 'ROLLBACK_REFUSED: disable every published process that requires readiness before rolling back';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "RequiresReadiness",
                table: "business_processes");
        }
    }
}
