using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskHistoryDetailCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StateReasonArgsJson",
                table: "transport_tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateReasonCode",
                table: "transport_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetailArgsJson",
                table: "task_history",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetailCode",
                table: "task_history",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StateReasonArgsJson",
                table: "transport_tasks");

            migrationBuilder.DropColumn(
                name: "StateReasonCode",
                table: "transport_tasks");

            migrationBuilder.DropColumn(
                name: "DetailArgsJson",
                table: "task_history");

            migrationBuilder.DropColumn(
                name: "DetailCode",
                table: "task_history");
        }
    }
}
