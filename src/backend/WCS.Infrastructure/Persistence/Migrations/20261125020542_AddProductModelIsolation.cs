using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductModelIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Disposition",
                table: "racks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductModelId",
                table: "racks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductModelId",
                table: "locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StationRole",
                table: "locations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductModelId",
                table: "business_processes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_models",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_models", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_racks_CurrentLocationId_Disposition",
                table: "racks",
                columns: new[] { "CurrentLocationId", "Disposition" });

            migrationBuilder.CreateIndex(
                name: "IX_racks_ProductModelId",
                table: "racks",
                column: "ProductModelId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_ProductModelId",
                table: "locations",
                column: "ProductModelId");

            migrationBuilder.CreateIndex(
                name: "IX_business_processes_ProductModelId",
                table: "business_processes",
                column: "ProductModelId");

            migrationBuilder.CreateIndex(
                name: "IX_product_models_Code",
                table: "product_models",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_business_processes_product_models_ProductModelId",
                table: "business_processes",
                column: "ProductModelId",
                principalTable: "product_models",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_locations_product_models_ProductModelId",
                table: "locations",
                column: "ProductModelId",
                principalTable: "product_models",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_racks_product_models_ProductModelId",
                table: "racks",
                column: "ProductModelId",
                principalTable: "product_models",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CR-036: a rollback must never make a rack that was handed to manual handling dispatchable again, nor silently drop model data.
            migrationBuilder.Sql(@"
DO $$
DECLARE manual_racks integer; modelled_racks integer; modelled_locations integer; modelled_processes integer; roles integer; models integer;
BEGIN
    SELECT COUNT(*) INTO manual_racks FROM racks WHERE ""Disposition"" <> 'Auto';
    SELECT COUNT(*) INTO modelled_racks FROM racks WHERE ""ProductModelId"" IS NOT NULL;
    SELECT COUNT(*) INTO modelled_locations FROM locations WHERE ""ProductModelId"" IS NOT NULL;
    SELECT COUNT(*) INTO roles FROM locations WHERE ""StationRole"" IS NOT NULL;
    SELECT COUNT(*) INTO modelled_processes FROM business_processes WHERE ""ProductModelId"" IS NOT NULL;
    SELECT COUNT(*) INTO models FROM product_models;
    IF manual_racks > 0 OR modelled_racks > 0 OR modelled_locations > 0 OR roles > 0 OR modelled_processes > 0 OR models > 0 THEN
        RAISE EXCEPTION 'ROLLBACK_REFUSED (AddProductModelIsolation): % rack(s) in manual handling, % rack(s), % location(s), % process(es) with a product model, % station role(s), % model(s) exist. Nothing was changed. Dropping the columns would lose this data and would make racks in manual handling dispatchable again.', manual_racks, modelled_racks, modelled_locations, modelled_processes, roles, models;
    END IF;
END $$;");

            migrationBuilder.DropForeignKey(
                name: "FK_business_processes_product_models_ProductModelId",
                table: "business_processes");

            migrationBuilder.DropForeignKey(
                name: "FK_locations_product_models_ProductModelId",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "FK_racks_product_models_ProductModelId",
                table: "racks");

            migrationBuilder.DropTable(
                name: "product_models");

            migrationBuilder.DropIndex(
                name: "IX_racks_CurrentLocationId_Disposition",
                table: "racks");

            migrationBuilder.DropIndex(
                name: "IX_racks_ProductModelId",
                table: "racks");

            migrationBuilder.DropIndex(
                name: "IX_locations_ProductModelId",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_business_processes_ProductModelId",
                table: "business_processes");

            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "racks");

            migrationBuilder.DropColumn(
                name: "ProductModelId",
                table: "racks");

            migrationBuilder.DropColumn(
                name: "ProductModelId",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "StationRole",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "ProductModelId",
                table: "business_processes");
        }
    }
}
