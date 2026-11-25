using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionModeAndRcsMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutionMode",
                table: "transport_tasks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionMode",
                table: "transport_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "execution_mode_state",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsExplicit = table.Column<bool>(type: "boolean", nullable: false),
                    RecoveryRequired = table.Column<bool>(type: "boolean", nullable: false),
                    RecoveryReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_mode_state", x => x.Id);
                    table.CheckConstraint("ck_execution_mode_state_single", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "execution_mode_transitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ToMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_mode_transitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rcs_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RcsTaskCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Protocol = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    HttpMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    PayloadTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    Result = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rcs_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_execution_mode_transitions_At",
                table: "execution_mode_transitions",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_execution_mode_transitions_RequestId",
                table: "execution_mode_transitions",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rcs_messages_CreatedAt",
                table: "rcs_messages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_rcs_messages_RequestCode",
                table: "rcs_messages",
                column: "RequestCode");

            migrationBuilder.CreateIndex(
                name: "IX_rcs_messages_Seq",
                table: "rcs_messages",
                column: "Seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rcs_messages_TaskId_CreatedAt",
                table: "rcs_messages",
                columns: new[] { "TaskId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "execution_mode_state");

            migrationBuilder.DropTable(
                name: "execution_mode_transitions");

            migrationBuilder.DropTable(
                name: "rcs_messages");

            migrationBuilder.DropColumn(
                name: "ExecutionMode",
                table: "transport_tasks");

            migrationBuilder.DropColumn(
                name: "ExecutionMode",
                table: "transport_orders");
        }
    }
}
