using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <summary>
    /// Widens every <c>ck_&lt;table&gt;_source</c> check constraint to admit <c>'chat'</c>, the fifth
    /// <see cref="CarTracker.Shared.EntrySource"/> (the in-app assistant).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fifteen constraint pairs in one migration is expected, not accidental: <c>ConfigureAudit&lt;T&gt;</c> is
    /// applied by every <c>IAuditable</c> entity's configuration, so widening the set widens all of them. The
    /// count is the point — a table missing from this diff is a table whose configuration forgot the audit
    /// block, and the first chat write against it would fail the old constraint at runtime.
    /// </para>
    /// <para>
    /// <b>No column widening.</b> <c>'chat'</c> is four characters and the column is <c>varchar(8)</c>.
    /// </para>
    /// <para>
    /// <b><c>Down</c> will fail if any <c>'chat'</c> row exists, and that is correct.</b> The alternative —
    /// rewriting real attribution to some other surface so the rollback succeeds — would destroy the only
    /// evidence of which surface produced a figure, which is the whole reason the column exists.
    /// </para>
    /// </remarks>
    public partial class AddChatEntrySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_wash_entries_source",
                table: "wash_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vehicles_source",
                table: "vehicles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tyre_readings_source",
                table: "tyre_readings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_records_source",
                table: "service_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mileage_readings_source",
                table: "mileage_readings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_tasks_source",
                table: "maintenance_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_source",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_entries_source",
                table: "fuel_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expense_entries_source",
                table: "expense_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_equipment_items_source",
                table: "equipment_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_source",
                table: "documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_data_anomalies_source",
                table: "data_anomalies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_logs_source",
                table: "check_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_definitions_source",
                table: "check_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_budget_groups_source",
                table: "budget_groups");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wash_entries_source",
                table: "wash_entries",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehicles_source",
                table: "vehicles",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tyre_readings_source",
                table: "tyre_readings",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_records_source",
                table: "service_records",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_mileage_readings_source",
                table: "mileage_readings",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_tasks_source",
                table: "maintenance_tasks",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_source",
                table: "issues",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_entries_source",
                table: "fuel_entries",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expense_entries_source",
                table: "expense_entries",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_equipment_items_source",
                table: "equipment_items",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_source",
                table: "documents",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_data_anomalies_source",
                table: "data_anomalies",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_logs_source",
                table: "check_logs",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_definitions_source",
                table: "check_definitions",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_budget_groups_source",
                table: "budget_groups",
                sql: "source IN ('web', 'mcp', 'import', 'seed', 'chat')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_wash_entries_source",
                table: "wash_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vehicles_source",
                table: "vehicles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tyre_readings_source",
                table: "tyre_readings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_records_source",
                table: "service_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mileage_readings_source",
                table: "mileage_readings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_tasks_source",
                table: "maintenance_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_source",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_entries_source",
                table: "fuel_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expense_entries_source",
                table: "expense_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_equipment_items_source",
                table: "equipment_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_source",
                table: "documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_data_anomalies_source",
                table: "data_anomalies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_logs_source",
                table: "check_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_definitions_source",
                table: "check_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_budget_groups_source",
                table: "budget_groups");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wash_entries_source",
                table: "wash_entries",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehicles_source",
                table: "vehicles",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tyre_readings_source",
                table: "tyre_readings",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_records_source",
                table: "service_records",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_mileage_readings_source",
                table: "mileage_readings",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_tasks_source",
                table: "maintenance_tasks",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_source",
                table: "issues",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_entries_source",
                table: "fuel_entries",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expense_entries_source",
                table: "expense_entries",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_equipment_items_source",
                table: "equipment_items",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_source",
                table: "documents",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_data_anomalies_source",
                table: "data_anomalies",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_logs_source",
                table: "check_logs",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_definitions_source",
                table: "check_definitions",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_budget_groups_source",
                table: "budget_groups",
                sql: "source IN ('web', 'mcp', 'import', 'seed')");
        }
    }
}
