using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRcsMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rcs_maps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MapId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MapName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MapQrCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MapType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RowCount = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ColCount = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Width = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Height = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PointCount = table.Column<int>(type: "integer", nullable: false),
                    EdgeCount = table.Column<int>(type: "integer", nullable: false),
                    GeometryJson = table.Column<string>(type: "text", nullable: false),
                    SourceXml = table.Column<string>(type: "text", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ImportedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MmPerUnit = table.Column<double>(type: "double precision", nullable: false),
                    YAxisUp = table.Column<bool>(type: "boolean", nullable: false),
                    HeadingOffsetDeg = table.Column<double>(type: "double precision", nullable: false),
                    HeadingClockwise = table.Column<bool>(type: "boolean", nullable: false),
                    RcsMapCodes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rcs_maps", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rcs_maps");
        }
    }
}
