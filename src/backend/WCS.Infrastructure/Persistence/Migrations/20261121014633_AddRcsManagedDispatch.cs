using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRcsManagedDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_transport_tasks_one_active_per_agv",
                table: "transport_tasks");

            migrationBuilder.AlterColumn<Guid>(
                name: "AgvId",
                table: "transport_tasks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignedAt",
                table: "transport_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignmentCheckedAt",
                table: "transport_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignmentSyncFailures",
                table: "transport_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RcsDispatchState",
                table: "transport_tasks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RobotCode",
                table: "transport_tasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_tasks_RobotCode",
                table: "transport_tasks",
                column: "RobotCode");

            migrationBuilder.CreateIndex(
                name: "ux_transport_tasks_one_active_per_agv",
                table: "transport_tasks",
                column: "AgvId",
                unique: true,
                filter: "\"AgvId\" IS NOT NULL AND \"RcsDispatchState\" IS NULL AND \"State\" IN ('Dispatching', 'Accepted', 'Executing', 'WaitingForPlc', 'WaitingForDestination', 'Continuing', 'CancelPending')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transport_tasks_RobotCode",
                table: "transport_tasks");

            migrationBuilder.DropIndex(
                name: "ux_transport_tasks_one_active_per_agv",
                table: "transport_tasks");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "transport_tasks");

            migrationBuilder.DropColumn(
                name: "AssignmentCheckedAt",
                table: "transport_tasks");

            migrationBuilder.DropColumn(
                name: "AssignmentSyncFailures",
                table: "transport_tasks");

            migrationBuilder.DropColumn(
                name: "RcsDispatchState",
                table: "transport_tasks");

            migrationBuilder.DropColumn(
                name: "RobotCode",
                table: "transport_tasks");

            migrationBuilder.AlterColumn<Guid>(
                name: "AgvId",
                table: "transport_tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_transport_tasks_one_active_per_agv",
                table: "transport_tasks",
                column: "AgvId",
                unique: true,
                filter: "\"State\" IN ('Dispatching', 'Accepted', 'Executing', 'WaitingForPlc', 'WaitingForDestination', 'Continuing', 'CancelPending')");
        }
    }
}
