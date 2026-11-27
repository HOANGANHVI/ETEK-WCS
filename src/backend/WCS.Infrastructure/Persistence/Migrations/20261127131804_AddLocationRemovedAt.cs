using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <summary>CR-057: a location can be removed while its row stays for history. One nullable column; the unique code index then covers only the locations that are not removed. No data change.</summary>
    public partial class AddLocationRemovedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_locations_Code",
                table: "locations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemovedAt",
                table: "locations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_locations_Code",
                table: "locations",
                column: "Code",
                unique: true,
                filter: "\"RemovedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CR-057: dropping the column would bring removed locations back (and a reused code would break the unique index): stop with a clear reason
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM locations WHERE "RemovedAt" IS NOT NULL) THEN
                        RAISE EXCEPTION 'ROLLBACK_REFUSED: removed locations exist; restore the database from the backup instead';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_locations_Code",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "locations");

            migrationBuilder.CreateIndex(
                name: "IX_locations_Code",
                table: "locations",
                column: "Code",
                unique: true);
        }
    }
}
