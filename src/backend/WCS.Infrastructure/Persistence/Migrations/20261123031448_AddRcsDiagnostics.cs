using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRcsDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rcs_api_tests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Api = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Group = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequestId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RequestJson = table.Column<string>(type: "text", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    ResponseTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    RcsCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RcsMessage = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rcs_api_tests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rcs_callback_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Callback = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceAddress = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    RequestJson = table.Column<string>(type: "text", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    Processed = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rcs_callback_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rcs_api_tests_Api_ExecutedAt",
                table: "rcs_api_tests",
                columns: new[] { "Api", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_rcs_api_tests_ExecutedAt",
                table: "rcs_api_tests",
                column: "ExecutedAt");

            migrationBuilder.CreateIndex(
                name: "IX_rcs_callback_log_ReceivedAt",
                table: "rcs_callback_log",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rcs_api_tests");

            migrationBuilder.DropTable(
                name: "rcs_callback_log");
        }
    }
}
