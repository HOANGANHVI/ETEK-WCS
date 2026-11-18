using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlcWriteApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WriteApproval",
                table: "station_io_points",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotApproved");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WriteDecidedAt",
                table: "station_io_points",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WriteDecidedBy",
                table: "station_io_points",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WriteDecisionNote",
                table: "station_io_points",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Points whose write permission was granted before this change keep it: they become Approved (decided by the earlier read approver when known).
            migrationBuilder.Sql("UPDATE station_io_points SET \"WriteApproval\" = 'Approved', \"WriteDecidedBy\" = \"ApprovedBy\", \"WriteDecidedAt\" = \"ApprovedAt\" WHERE \"WriteAllowed\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WriteApproval",
                table: "station_io_points");

            migrationBuilder.DropColumn(
                name: "WriteDecidedAt",
                table: "station_io_points");

            migrationBuilder.DropColumn(
                name: "WriteDecidedBy",
                table: "station_io_points");

            migrationBuilder.DropColumn(
                name: "WriteDecisionNote",
                table: "station_io_points");
        }
    }
}
