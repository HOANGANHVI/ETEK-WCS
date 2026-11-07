using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMcAddressBasesAndTimeouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConnectTimeoutMs",
                table: "plc_connections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadTimeoutMs",
                table: "plc_connections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WAddressBase",
                table: "plc_connections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WriteTimeoutMs",
                table: "plc_connections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "XyAddressBase",
                table: "plc_connections",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectTimeoutMs",
                table: "plc_connections");

            migrationBuilder.DropColumn(
                name: "ReadTimeoutMs",
                table: "plc_connections");

            migrationBuilder.DropColumn(
                name: "WAddressBase",
                table: "plc_connections");

            migrationBuilder.DropColumn(
                name: "WriteTimeoutMs",
                table: "plc_connections");

            migrationBuilder.DropColumn(
                name: "XyAddressBase",
                table: "plc_connections");
        }
    }
}
