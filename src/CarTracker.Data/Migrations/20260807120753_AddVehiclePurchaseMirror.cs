using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVehiclePurchaseMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_vehicle_purchase",
                table: "expense_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "wash_entry_id",
                table: "expense_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_entries_wash_entry_id",
                table: "expense_entries",
                column: "wash_entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_expense_entries_vehicle_purchase",
                table: "expense_entries",
                column: "vehicle_id",
                unique: true,
                filter: "is_vehicle_purchase");

            migrationBuilder.AddForeignKey(
                name: "fk_expense_entries_wash_entries_wash_entry_id",
                table: "expense_entries",
                column: "wash_entry_id",
                principalTable: "wash_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // ---- backfill 1: adopt any hand-typed Purchase row as the vehicle's mirror ------------------
            // Before inserting anything, claim what is already there. A vehicle that already carries a
            // Purchase-category expense has its purchase recorded once; inserting a second from
            // vehicles.purchase_price would double the largest line in the log — precisely the £163.16
            // double-count this mirror exists to prevent. DISTINCT ON keeps one row per vehicle (earliest,
            // then lowest id) so the partial unique index cannot be violated by a vehicle with several.
            migrationBuilder.Sql("""
                UPDATE expense_entries e
                SET is_vehicle_purchase = true
                FROM (
                    SELECT DISTINCT ON (vehicle_id) id
                    FROM expense_entries
                    WHERE category = 'Purchase'
                    ORDER BY vehicle_id, entry_date, id
                ) pick
                WHERE e.id = pick.id;
                """);

            // ---- backfill 2: mirror vehicles.purchase_price for everyone left -------------------------
            // The orphan this migration exists to close: a stored purchase price that reached no figure.
            // The mirror inherits the vehicle's own source, as the fuel mirror inherits the fill's.
            migrationBuilder.Sql("""
                INSERT INTO expense_entries
                    (vehicle_id, entry_date, category, vendor, amount, mileage,
                     is_vehicle_purchase, created_at, updated_at, source)
                SELECT v.id, v.purchase_date, 'Purchase', v.seller, v.purchase_price, v.purchase_mileage,
                       true, now(), now(), v.source
                FROM vehicles v
                WHERE v.purchase_price IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM expense_entries e
                      WHERE e.vehicle_id = v.id AND e.is_vehicle_purchase);
                """);

            // ---- backfill 3: mirror wash costs --------------------------------------------------------
            // wash_entries.cost was shown on the wash screen and counted nowhere.
            migrationBuilder.Sql("""
                INSERT INTO expense_entries
                    (vehicle_id, entry_date, category, vendor, amount, mileage,
                     wash_entry_id, created_at, updated_at, source)
                SELECT w.vehicle_id, w.wash_date, 'Wash', w.location, w.cost, w.mileage,
                       w.id, now(), now(), w.source
                FROM wash_entries w
                WHERE w.cost IS NOT NULL AND w.cost > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM expense_entries e WHERE e.wash_entry_id = w.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Wash mirrors are unambiguously this migration's work — wash_entry_id names its source — so they
            // go. Purchase rows do not come back out: backfill 1 *adopted* rows a user had already typed, and
            // after the flag column is dropped nothing distinguishes those from the ones backfill 2 inserted.
            // Deleting a person's expense row on a rollback is the worse error, and leaving one behind is
            // harmless — re-running Up adopts rather than duplicates, so the cycle stays idempotent.
            migrationBuilder.Sql("DELETE FROM expense_entries WHERE wash_entry_id IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "fk_expense_entries_wash_entries_wash_entry_id",
                table: "expense_entries");

            migrationBuilder.DropIndex(
                name: "ix_expense_entries_wash_entry_id",
                table: "expense_entries");

            migrationBuilder.DropIndex(
                name: "ux_expense_entries_vehicle_purchase",
                table: "expense_entries");

            migrationBuilder.DropColumn(
                name: "is_vehicle_purchase",
                table: "expense_entries");

            migrationBuilder.DropColumn(
                name: "wash_entry_id",
                table: "expense_entries");
        }
    }
}
