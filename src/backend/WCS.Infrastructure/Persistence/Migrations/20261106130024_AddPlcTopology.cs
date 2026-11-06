using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlcTopology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plc_comm_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlcId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    StationId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Event = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plc_comm_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plc_connections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: true),
                    Protocol = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    NetworkNumber = table.Column<int>(type: "integer", nullable: true),
                    PcNumber = table.Column<int>(type: "integer", nullable: true),
                    ModuleIoNumber = table.Column<int>(type: "integer", nullable: true),
                    PlcStationNumber = table.Column<int>(type: "integer", nullable: true),
                    MonitoringTimer = table.Column<int>(type: "integer", nullable: true),
                    TimeoutMs = table.Column<int>(type: "integer", nullable: true),
                    PollingIntervalMs = table.Column<int>(type: "integer", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plc_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plc_stations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlcId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    LocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plc_stations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_plc_stations_plc_connections_PlcId",
                        column: x => x.PlcId,
                        principalTable: "plc_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "station_io_points",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlcId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StationId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Seq = table.Column<int>(type: "integer", nullable: false),
                    Group = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SignalName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Direction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DataType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TemplateAddressReference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActualAddress = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    WordLength = table.Column<int>(type: "integer", nullable: true),
                    Encoding = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WriteAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_station_io_points", x => x.Id);
                    table.ForeignKey(
                        name: "FK_station_io_points_plc_connections_PlcId",
                        column: x => x.PlcId,
                        principalTable: "plc_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_station_io_points_plc_stations_StationId",
                        column: x => x.StationId,
                        principalTable: "plc_stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_plc_comm_log_OccurredAt",
                table: "plc_comm_log",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_plc_comm_log_PlcId_OccurredAt",
                table: "plc_comm_log",
                columns: new[] { "PlcId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_plc_stations_PlcId_Order",
                table: "plc_stations",
                columns: new[] { "PlcId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_station_io_points_PlcId_ActualAddress",
                table: "station_io_points",
                columns: new[] { "PlcId", "ActualAddress" },
                unique: true,
                filter: "\"ActualAddress\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_station_io_points_PlcId_StationId_SignalName",
                table: "station_io_points",
                columns: new[] { "PlcId", "StationId", "SignalName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_station_io_points_StationId",
                table: "station_io_points",
                column: "StationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plc_comm_log");

            migrationBuilder.DropTable(
                name: "station_io_points");

            migrationBuilder.DropTable(
                name: "plc_stations");

            migrationBuilder.DropTable(
                name: "plc_connections");
        }
    }
}
