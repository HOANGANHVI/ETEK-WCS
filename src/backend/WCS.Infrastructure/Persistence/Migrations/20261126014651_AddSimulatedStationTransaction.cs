using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulatedStationTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Simulated",
                table: "station_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CR-041: without the column an unfinished simulated request would later be resumed as a real PLC request (possibly in Real mode): refuse the rollback while one exists.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM station_transactions WHERE "Simulated" AND "OrderId" IS NULL AND "State" = 'Accepted') THEN
                    RAISE EXCEPTION 'ROLLBACK_REFUSED: simulated requests are still waiting to start; resolve them first.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "Simulated",
                table: "station_transactions");
        }
    }
}
