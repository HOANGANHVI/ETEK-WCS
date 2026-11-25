using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransportQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "transport_queue_seq");

            migrationBuilder.AddColumn<bool>(
                name: "QueueHeld",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "QueueScope",
                table: "transport_orders",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QueueSequence",
                table: "transport_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QueueScope",
                table: "business_processes",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_QueueScope_QueueSequence",
                table: "transport_orders",
                columns: new[] { "QueueScope", "QueueSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_QueueSequence",
                table: "transport_orders",
                column: "QueueSequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CR-040: the earlier version does not order requests; dropping the queue under open queued requests would let a newer one overtake an older one.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM transport_orders WHERE "QueueSequence" IS NOT NULL AND "State" NOT IN ('Completed', 'Failed', 'Cancelled')) THEN
                        RAISE EXCEPTION 'ROLLBACK_REFUSED: finish or cancel every open queued request before rolling back';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_QueueScope_QueueSequence",
                table: "transport_orders");

            migrationBuilder.DropIndex(
                name: "IX_transport_orders_QueueSequence",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "QueueHeld",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "QueueScope",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "QueueSequence",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "QueueScope",
                table: "business_processes");

            migrationBuilder.DropSequence(
                name: "transport_queue_seq");
        }
    }
}
