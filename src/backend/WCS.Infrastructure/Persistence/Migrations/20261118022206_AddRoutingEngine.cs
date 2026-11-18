using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_processes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RequestType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RackKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TriggerLocationCodesCsv = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReviewBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_processes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "station_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlcId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Operation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LegId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    RackKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    HandshakeStep = table.Column<int>(type: "integer", nullable: false),
                    AgvId = table.Column<Guid>(type: "uuid", nullable: true),
                    Suspended = table.Column<bool>(type: "boolean", nullable: false),
                    SuspendedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SuspendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReconciliationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_station_transactions", x => x.Id);
                    table.CheckConstraint("ck_station_transactions_step", "\"HandshakeStep\" BETWEEN 0 AND 9");
                    table.ForeignKey(
                        name: "FK_station_transactions_plc_connections_PlcId",
                        column: x => x.PlcId,
                        principalTable: "plc_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_station_transactions_plc_stations_PortId",
                        column: x => x.PortId,
                        principalTable: "plc_stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_process_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidationJson = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    FirstStepKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_process_versions", x => x.Id);
                    table.CheckConstraint("ck_process_versions_number", "\"Number\" > 0");
                    table.ForeignKey(
                        name: "FK_business_process_versions_business_processes_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "business_processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "route_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DestinationMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NextStepKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    AlternativeNextStepKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    WaitBehavior = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ErrorBehavior = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ConditionsJson = table.Column<string>(type: "text", nullable: true),
                    WaitingPositionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_route_steps_business_process_versions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "business_process_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transport_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ProcessVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    RackKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RackId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    WaitKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StateReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CurrentStepKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    LegCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StateEnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_orders_business_process_versions_ProcessVersionId",
                        column: x => x.ProcessVersionId,
                        principalTable: "business_process_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_orders_business_processes_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "business_processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "route_candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    LocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConditionsJson = table.Column<string>(type: "text", nullable: true),
                    NoBind = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_candidates", x => x.Id);
                    table.CheckConstraint("ck_route_candidates_priority", "\"Priority\" > 0");
                    table.ForeignKey(
                        name: "FK_route_candidates_route_steps_StepId",
                        column: x => x.StepId,
                        principalTable: "route_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "routing_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_routing_events_transport_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transport_legs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    StepKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    StepType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DestinationLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DestinationNoBind = table.Column<bool>(type: "boolean", nullable: false),
                    RackKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RackId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgvId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgvRequestCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AgvTaskCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    WaitKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StateReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PickConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DropConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StateEnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_legs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_legs_agvs_AgvId",
                        column: x => x.AgvId,
                        principalTable: "agvs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_legs_transport_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_process_versions_ProcessId_Number",
                table: "business_process_versions",
                columns: new[] { "ProcessId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_process_versions_one_published",
                table: "business_process_versions",
                column: "ProcessId",
                unique: true,
                filter: "\"State\" = 'Published'");

            migrationBuilder.CreateIndex(
                name: "IX_business_processes_Code",
                table: "business_processes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_route_candidates_StepId_Role_Priority_Order",
                table: "route_candidates",
                columns: new[] { "StepId", "Role", "Priority", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_route_steps_VersionId_StepKey",
                table: "route_steps",
                columns: new[] { "VersionId", "StepKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_routing_events_OrderId_OccurredAt",
                table: "routing_events",
                columns: new[] { "OrderId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_station_transactions_LegId",
                table: "station_transactions",
                column: "LegId");

            migrationBuilder.CreateIndex(
                name: "IX_station_transactions_OrderId",
                table: "station_transactions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_station_transactions_PlcId_State",
                table: "station_transactions",
                columns: new[] { "PlcId", "State" });

            migrationBuilder.CreateIndex(
                name: "ux_station_transactions_one_active_per_port",
                table: "station_transactions",
                column: "PortId",
                unique: true,
                filter: "\"State\" NOT IN ('Completed', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "ux_station_transactions_request_id",
                table: "station_transactions",
                columns: new[] { "PlcId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transport_legs_AgvRequestCode",
                table: "transport_legs",
                column: "AgvRequestCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_legs_OrderId_Sequence",
                table: "transport_legs",
                columns: new[] { "OrderId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_legs_State_CreatedAt",
                table: "transport_legs",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ux_transport_legs_one_active_per_agv",
                table: "transport_legs",
                column: "AgvId",
                unique: true,
                filter: "\"AgvId\" IS NOT NULL AND \"State\" IN ('InProgress', 'Waiting')");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_ProcessId",
                table: "transport_orders",
                column: "ProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_ProcessVersionId",
                table: "transport_orders",
                column: "ProcessVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_RequestKey",
                table: "transport_orders",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_State",
                table: "transport_orders",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "route_candidates");

            migrationBuilder.DropTable(
                name: "routing_events");

            migrationBuilder.DropTable(
                name: "station_transactions");

            migrationBuilder.DropTable(
                name: "transport_legs");

            migrationBuilder.DropTable(
                name: "route_steps");

            migrationBuilder.DropTable(
                name: "transport_orders");

            migrationBuilder.DropTable(
                name: "business_process_versions");

            migrationBuilder.DropTable(
                name: "business_processes");
        }
    }
}
