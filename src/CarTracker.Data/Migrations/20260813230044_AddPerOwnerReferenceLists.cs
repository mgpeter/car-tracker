using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <summary>
    /// Moves the three reference lists — garages, wash locations, expense categories — from being global to
    /// belonging to an account, keyed <c>(owner_id, name)</c>, and drops the six foreign keys that pointed at
    /// their old single-column key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hand-written, not scaffolded.</b> EF's generated <c>Up()</c> was thrown away: it adds
    /// <c>owner_id NOT NULL DEFAULT 0</c> (no such user, so the new foreign key cannot hold), and its
    /// <c>DeleteData</c> for the 13 seeded categories is keyed on the old primary key, so it would eat the
    /// per-user copies this migration has just made. The order below is the whole point of the file, and every
    /// step is a precondition of the next:
    /// </para>
    /// <list type="number">
    /// <item>refuse to run on a multi-account deployment (see below);</item>
    /// <item>drop the six child foreign keys, or the deletes and copies below break them mid-flight;</item>
    /// <item>drop the three single-column primary keys, or a per-user copy collides on the name;</item>
    /// <item>add <c>owner_id</c> <b>nullable</b>, because the existing rows have no owner yet;</item>
    /// <item>copy every row once per user;</item>
    /// <item>delete the ownerless originals;</item>
    /// <item>only now <c>SET NOT NULL</c>, add the composite keys, and add the owner foreign keys.</item>
    /// </list>
    /// <para>
    /// <b>Why it aborts above one user.</b> The backfill copies every list to every account, which is right for
    /// the one existing deployment (one user, whose lists these already are) and wrong for any other: a garage
    /// row carries a contact, an address and free-text notes, so handing every account a copy of everyone's
    /// would be a data leak dressed as a migration. There is no way to attribute a global row to the account
    /// that typed it — nothing recorded that — so the migration refuses rather than guesses. If this ever fires,
    /// the answer is a hand-written attribution script, not a looser assertion.
    /// </para>
    /// <para>
    /// <b>The six child columns are untouched.</b> <c>service_records.garage</c>,
    /// <c>maintenance_tasks.assigned_garage</c>, <c>vehicles.default_garage</c>, <c>wash_entries.location</c>,
    /// <c>expense_entries.category</c> and <c>budget_group_categories.category</c> keep their types and their
    /// values; only the constraints go. That is what makes this migration small and the contract diff additive.
    /// </para>
    /// </remarks>
    public partial class AddPerOwnerReferenceLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 0. The precondition, enforced rather than trusted --------------------------------------------
            migrationBuilder.Sql("""
                DO $$
                DECLARE user_count integer;
                BEGIN
                    SELECT count(*) INTO user_count FROM users;
                    IF user_count > 1 THEN
                        RAISE EXCEPTION
                            'AddPerOwnerReferenceLists refuses to run: % accounts exist. Its backfill copies '
                            'every reference row to every account, which would hand each account the others'''
                            ' garage contacts, addresses and notes. Attribute the rows by hand first.',
                            user_count;
                    END IF;
                END $$;
                """);

            // ---- 1. The six foreign keys onto the old single-column keys ---------------------------------------
            migrationBuilder.DropForeignKey(name: "fk_service_records_garages_garage", table: "service_records");
            migrationBuilder.DropForeignKey(name: "fk_maintenance_tasks_garages_assigned_garage", table: "maintenance_tasks");
            migrationBuilder.DropForeignKey(name: "fk_vehicles_garages_default_garage", table: "vehicles");
            migrationBuilder.DropForeignKey(name: "fk_wash_entries_wash_locations_location", table: "wash_entries");
            migrationBuilder.DropForeignKey(name: "fk_expense_entries_expense_categories_category", table: "expense_entries");
            migrationBuilder.DropForeignKey(name: "fk_budget_group_categories_expense_categories_category", table: "budget_group_categories");

            // Their indexes existed only to serve those constraints, and the model no longer declares them.
            migrationBuilder.DropIndex(name: "ix_service_records_garage", table: "service_records");
            migrationBuilder.DropIndex(name: "ix_maintenance_tasks_assigned_garage", table: "maintenance_tasks");
            migrationBuilder.DropIndex(name: "ix_vehicles_default_garage", table: "vehicles");
            migrationBuilder.DropIndex(name: "ix_wash_entries_location", table: "wash_entries");
            migrationBuilder.DropIndex(name: "ix_expense_entries_category", table: "expense_entries");
            migrationBuilder.DropIndex(name: "ix_budget_group_categories_category", table: "budget_group_categories");

            // ---- 2. The three single-column primary keys ------------------------------------------------------
            migrationBuilder.DropPrimaryKey(name: "pk_garages", table: "garages");
            migrationBuilder.DropPrimaryKey(name: "pk_wash_locations", table: "wash_locations");
            migrationBuilder.DropPrimaryKey(name: "pk_expense_categories", table: "expense_categories");

            // ---- 3. owner_id, nullable for the length of the backfill ------------------------------------------
            migrationBuilder.Sql("ALTER TABLE garages ADD COLUMN owner_id integer NULL;");
            migrationBuilder.Sql("ALTER TABLE wash_locations ADD COLUMN owner_id integer NULL;");
            migrationBuilder.Sql("ALTER TABLE expense_categories ADD COLUMN owner_id integer NULL;");

            // ---- 4. One copy of every row per account ----------------------------------------------------------
            // A cross join, so a deployment with no users (a fresh checkout, CI) copies nothing and step 5 empties
            // the tables — which is correct: reference lists are created as used.
            migrationBuilder.Sql("""
                INSERT INTO garages (owner_id, name, contact, address, notes)
                SELECT u.id, g.name, g.contact, g.address, g.notes
                FROM garages g CROSS JOIN users u
                WHERE g.owner_id IS NULL;
                """);
            migrationBuilder.Sql("""
                INSERT INTO wash_locations (owner_id, name, notes)
                SELECT u.id, w.name, w.notes
                FROM wash_locations w CROSS JOIN users u
                WHERE w.owner_id IS NULL;
                """);
            migrationBuilder.Sql("""
                INSERT INTO expense_categories (owner_id, name, display_order, is_system)
                SELECT u.id, c.name, c.display_order, c.is_system
                FROM expense_categories c CROSS JOIN users u
                WHERE c.owner_id IS NULL;
                """);

            // ---- 5. The ownerless originals ---------------------------------------------------------------------
            // Safe only because step 1 removed the constraints that would have blanked or blocked the children,
            // and because step 4 has already made every copy. This is the step that deletes rows.
            migrationBuilder.Sql("DELETE FROM garages WHERE owner_id IS NULL;");
            migrationBuilder.Sql("DELETE FROM wash_locations WHERE owner_id IS NULL;");
            migrationBuilder.Sql("DELETE FROM expense_categories WHERE owner_id IS NULL;");

            // ---- 6. Now the column can be required ---------------------------------------------------------------
            migrationBuilder.Sql("ALTER TABLE garages ALTER COLUMN owner_id SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE wash_locations ALTER COLUMN owner_id SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE expense_categories ALTER COLUMN owner_id SET NOT NULL;");

            // ---- 7. The composite keys and the owner foreign keys ------------------------------------------------
            migrationBuilder.AddPrimaryKey(name: "pk_garages", table: "garages", columns: ["owner_id", "name"]);
            migrationBuilder.AddPrimaryKey(name: "pk_wash_locations", table: "wash_locations", columns: ["owner_id", "name"]);
            migrationBuilder.AddPrimaryKey(name: "pk_expense_categories", table: "expense_categories", columns: ["owner_id", "name"]);

            // Cascade, unlike vehicles.owner_id and assistant_tokens.owner_id, which are Restrict: a list entry
            // cannot outlive its list. No index on owner_id — it leads the primary key.
            migrationBuilder.AddForeignKey(
                name: "fk_garages_users_owner_id",
                table: "garages",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_wash_locations_users_owner_id",
                table: "wash_locations",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_expense_categories_users_owner_id",
                table: "expense_categories",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        /// <remarks>
        /// There is no honest down. Going back means collapsing every account's list into one global list, and
        /// the only ways to do that are to pick a winner per name — silently giving one account's garage address
        /// to everyone else — or to drop the lot. Restoring the six foreign keys would then blank or block the
        /// child rows that no longer match. Restore from a backup instead.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException(
                "AddPerOwnerReferenceLists cannot be reverted: collapsing per-account reference lists back into "
                + "one global list would have to pick a winner per name and would blank the child rows that lost. "
                + "Restore from a backup taken before it ran.");
    }
}
