using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrelationIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "transport_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "transport_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_tasks_CorrelationId",
                table: "transport_tasks",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_CorrelationId",
                table: "transport_orders",
                column: "CorrelationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transport_tasks_CorrelationId",
                table: "transport_tasks");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_CorrelationId",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "transport_tasks");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "transport_orders");
        }
    }
}
