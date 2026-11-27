using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIoDefaultAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_station_io_points_PlcId_ActualAddress",
                table: "station_io_points");

            migrationBuilder.AddColumn<string>(
                name: "AddressOrigin",
                table: "station_io_points",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "SharedWith",
                table: "station_io_points",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAt",
                table: "plc_stations",
                type: "timestamp with time zone",
                nullable: true);

            // addresses that already exist were entered by an Admin: they stay as they are and the catalog never overwrites them
            migrationBuilder.Sql("UPDATE station_io_points SET \"AddressOrigin\" = 'Custom' WHERE \"ActualAddress\" IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "retired_io_points",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CatalogSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlcId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StationId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SignalName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ActualAddress = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retired_io_points", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_station_io_points_PlcId_ActualAddress",
                table: "station_io_points",
                columns: new[] { "PlcId", "ActualAddress" },
                unique: true,
                filter: "\"ActualAddress\" IS NOT NULL AND \"SharedWith\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_station_io_points_PlcId_ActualAddress_SignalName",
                table: "station_io_points",
                columns: new[] { "PlcId", "ActualAddress", "SignalName" },
                unique: true,
                filter: "\"ActualAddress\" IS NOT NULL AND \"SharedWith\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_retired_io_points_PlcId_StationId",
                table: "retired_io_points",
                columns: new[] { "PlcId", "StationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CR-048: once the upgrade has run, going back would delete the archived rows and the old unique index could not hold the shared registers: refuse (restore the backup instead).
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM retired_io_points)
                     OR EXISTS (SELECT 1 FROM plc_stations WHERE "RetiredAt" IS NOT NULL)
                     OR EXISTS (SELECT 1 FROM station_io_points WHERE "ActualAddress" IS NOT NULL GROUP BY "PlcId", "ActualAddress" HAVING count(*) > 1) THEN
                    RAISE EXCEPTION 'ROLLBACK_REFUSED: the IO map upgrade has run (archived rows or shared registers exist); restore the database backup taken before the upgrade.';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "retired_io_points");

            migrationBuilder.DropIndex(
                name: "IX_station_io_points_PlcId_ActualAddress",
                table: "station_io_points");

            migrationBuilder.DropIndex(
                name: "IX_station_io_points_PlcId_ActualAddress_SignalName",
                table: "station_io_points");

            migrationBuilder.DropColumn(
                name: "AddressOrigin",
                table: "station_io_points");

            migrationBuilder.DropColumn(
                name: "SharedWith",
                table: "station_io_points");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "plc_stations");

            migrationBuilder.CreateIndex(
                name: "IX_station_io_points_PlcId_ActualAddress",
                table: "station_io_points",
                columns: new[] { "PlcId", "ActualAddress" },
                unique: true,
                filter: "\"ActualAddress\" IS NOT NULL");
        }
    }
}
