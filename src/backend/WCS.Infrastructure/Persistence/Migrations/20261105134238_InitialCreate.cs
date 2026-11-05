using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agvs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RcsRobotCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectionStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StatusDetail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastStatusAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agvs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RackId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    StatusBefore = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    StatusAfter = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Bind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RackCapacity = table.Column<int>(type: "integer", nullable: false),
                    AllowedRackKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresPlcPermission = table.Column<bool>(type: "boolean", nullable: false),
                    ReconciliationRequired = table.Column<bool>(type: "boolean", nullable: false),
                    LastInventoryChangeAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locations", x => x.Id);
                    table.CheckConstraint("ck_locations_capacity", "\"RackCapacity\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RackId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "racks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalRackId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RackType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CurrentLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_racks", x => x.Id);
                    table.CheckConstraint("ck_racks_transit_no_location", "\"Status\" <> 'InTransit' OR \"CurrentLocationId\" IS NULL");
                    table.ForeignKey(
                        name: "FK_racks_locations_CurrentLocationId",
                        column: x => x.CurrentLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transport_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Flow = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RackKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AgvId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RackId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CurrentLeg = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PlcPermissionGranted = table.Column<bool>(type: "boolean", nullable: false),
                    StateReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RcsRequestCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RcsTaskCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DispatchAttempts = table.Column<int>(type: "integer", nullable: false),
                    PickConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DropConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequiresReconciliation = table.Column<bool>(type: "boolean", nullable: false),
                    ReconciliationCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StateEnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_tasks_agvs_AgvId",
                        column: x => x.AgvId,
                        principalTable: "agvs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_tasks_locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_tasks_locations_RequestLocationId",
                        column: x => x.RequestLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_tasks_locations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ToState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    EventName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_history_transport_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "transport_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agvs_Code",
                table: "agvs",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agvs_Role",
                table: "agvs",
                column: "Role",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_FromLocationId",
                table: "inventory_transactions",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_OccurredAt",
                table: "inventory_transactions",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_TaskId",
                table: "inventory_transactions",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_ToLocationId",
                table: "inventory_transactions",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_Code",
                table: "locations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processed_events_Source_EventKey",
                table: "processed_events",
                columns: new[] { "Source", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_racks_CurrentLocationId",
                table: "racks",
                column: "CurrentLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_racks_ExternalRackId",
                table: "racks",
                column: "ExternalRackId",
                unique: true,
                filter: "\"ExternalRackId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_LocationId_Status",
                table: "reservations",
                columns: new[] { "LocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_reservations_RackId",
                table: "reservations",
                column: "RackId",
                unique: true,
                filter: "\"Status\" = 'Active' AND \"Kind\" = 'SourceRack' AND \"RackId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_TaskId",
                table: "reservations",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_task_history_TaskId_OccurredAt",
                table: "task_history",
                columns: new[] { "TaskId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_tasks_AgvId_State_CreatedAt",
                table: "transport_tasks",
                columns: new[] { "AgvId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_tasks_DestinationLocationId",
                table: "transport_tasks",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_tasks_RcsRequestCode",
                table: "transport_tasks",
                column: "RcsRequestCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_tasks_RcsTaskCode",
                table: "transport_tasks",
                column: "RcsTaskCode",
                unique: true,
                filter: "\"RcsTaskCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transport_tasks_RequestKey",
                table: "transport_tasks",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_tasks_RequestLocationId",
                table: "transport_tasks",
                column: "RequestLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_tasks_SourceLocationId",
                table: "transport_tasks",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_tasks_State",
                table: "transport_tasks",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "ux_transport_tasks_one_active_per_agv",
                table: "transport_tasks",
                column: "AgvId",
                unique: true,
                filter: "\"State\" IN ('Dispatching', 'Accepted', 'Executing', 'WaitingForPlc', 'WaitingForDestination', 'Continuing', 'CancelPending')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_transactions");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "racks");

            migrationBuilder.DropTable(
                name: "reservations");

            migrationBuilder.DropTable(
                name: "task_history");

            migrationBuilder.DropTable(
                name: "transport_tasks");

            migrationBuilder.DropTable(
                name: "agvs");

            migrationBuilder.DropTable(
                name: "locations");
        }
    }
}
