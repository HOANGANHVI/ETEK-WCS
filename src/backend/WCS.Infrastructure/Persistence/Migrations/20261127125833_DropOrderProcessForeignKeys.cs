using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WCS.Infrastructure.Persistence.Migrations
{
    /// <summary>CR-056: a process can be deleted while its finished orders stay (an order carries its own copy of the definition). Only the two foreign keys are dropped; no data and no index changes.</summary>
    public partial class DropOrderProcessForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transport_orders_business_process_versions_ProcessVersionId",
                table: "transport_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_transport_orders_business_processes_ProcessId",
                table: "transport_orders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CR-056: once a process with orders has been deleted the constraints cannot come back without losing history: stop with a clear reason (nothing is changed)
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM transport_orders o WHERE NOT EXISTS (SELECT 1 FROM business_processes p WHERE p."Id" = o."ProcessId")
                               OR NOT EXISTS (SELECT 1 FROM business_process_versions v WHERE v."Id" = o."ProcessVersionId")) THEN
                        RAISE EXCEPTION 'ROLLBACK_REFUSED: transport orders of a removed process exist; restore the database from the backup instead';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_transport_orders_business_process_versions_ProcessVersionId",
                table: "transport_orders",
                column: "ProcessVersionId",
                principalTable: "business_process_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transport_orders_business_processes_ProcessId",
                table: "transport_orders",
                column: "ProcessId",
                principalTable: "business_processes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
