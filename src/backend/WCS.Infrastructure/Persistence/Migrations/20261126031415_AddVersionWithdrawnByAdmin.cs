using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionWithdrawnByAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WithdrawnByAdmin",
                table: "business_process_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CR-042: without the column a queued request held because an Admin disabled its version could be dispatched by the previous build: refuse while one is open.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM transport_orders o JOIN business_process_versions v ON v."Id" = o."ProcessVersionId"
                             WHERE v."WithdrawnByAdmin" AND o."QueueSequence" IS NOT NULL AND o."LegCount" = 0 AND o."State" NOT IN ('Completed', 'Failed', 'Cancelled')) THEN
                    RAISE EXCEPTION 'ROLLBACK_REFUSED: queued requests are pinned to a version an Admin disabled; cancel them first.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "WithdrawnByAdmin",
                table: "business_process_versions");
        }
    }
}
