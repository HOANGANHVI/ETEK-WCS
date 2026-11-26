using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderRacksHeld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RacksHeld",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CR-042: without the column the racks a failed order still holds would become available again: refuse while one is held.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM transport_orders WHERE "RacksHeld") THEN
                    RAISE EXCEPTION 'ROLLBACK_REFUSED: failed orders still hold racks; release them first.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "RacksHeld",
                table: "transport_orders");
        }
    }
}
