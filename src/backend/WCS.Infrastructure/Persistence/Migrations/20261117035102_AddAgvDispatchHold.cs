using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgvDispatchHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DispatchHold",
                table: "agvs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchHoldAt",
                table: "agvs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchHoldReason",
                table: "agvs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DispatchHoldTaskId",
                table: "agvs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DispatchHold",
                table: "agvs");

            migrationBuilder.DropColumn(
                name: "DispatchHoldAt",
                table: "agvs");

            migrationBuilder.DropColumn(
                name: "DispatchHoldReason",
                table: "agvs");

            migrationBuilder.DropColumn(
                name: "DispatchHoldTaskId",
                table: "agvs");
        }
    }
}
