using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget_groups",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "varchar(40)", nullable: false),
                    annual_budget = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_groups", x => x.id);
                    table.CheckConstraint("ck_budget_groups_annual_budget", "annual_budget IS NULL OR annual_budget >= 0");
                    table.CheckConstraint("ck_budget_groups_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_budget_groups_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "budget_group_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    budget_group_id = table.Column<int>(type: "integer", nullable: false),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "varchar(24)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_group_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_budget_group_categories_budget_groups_budget_group_id",
                        column: x => x.budget_group_id,
                        principalTable: "budget_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_budget_group_categories_expense_categories_category",
                        column: x => x.category,
                        principalTable: "expense_categories",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budget_group_categories_budget_group_id",
                table: "budget_group_categories",
                column: "budget_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_group_categories_category",
                table: "budget_group_categories",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_budget_group_category_vehicle_category",
                table: "budget_group_categories",
                columns: new[] { "vehicle_id", "category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_budget_groups_vehicle_name",
                table: "budget_groups",
                columns: new[] { "vehicle_id", "name" },
                unique: true);

            // Migrate existing per-category targets into single-category groups named after their category, BEFORE
            // dropping the old table. A vehicle's four default groups only seed on NEW creation — existing cars
            // keep exactly the targets they had, one group per, and can be composed into multi-category groups in
            // the editor afterwards. (Multi-category groups cannot round-trip through Down; that is documented.)
            migrationBuilder.Sql(@"
                INSERT INTO budget_groups (vehicle_id, name, annual_budget, display_order, created_at, updated_at, source)
                SELECT vehicle_id, category, annual_budget, 0, created_at, updated_at, source
                FROM budget_categories;");

            migrationBuilder.Sql(@"
                INSERT INTO budget_group_categories (budget_group_id, vehicle_id, category)
                SELECT g.id, bc.vehicle_id, bc.category
                FROM budget_categories bc
                JOIN budget_groups g ON g.vehicle_id = bc.vehicle_id AND g.name = bc.category;");

            migrationBuilder.DropTable(
                name: "budget_categories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    annual_budget = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    category = table.Column<string>(type: "varchar(24)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_categories", x => x.id);
                    table.CheckConstraint("ck_budget_categories_annual_budget", "annual_budget >= 0");
                    table.CheckConstraint("ck_budget_categories_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_budget_categories_expense_categories_category",
                        column: x => x.category,
                        principalTable: "expense_categories",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_budget_categories_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budget_categories_category",
                table: "budget_categories",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_budget_vehicle_category",
                table: "budget_categories",
                columns: new[] { "vehicle_id", "category" },
                unique: true);

            // Best-effort back-fill: only single-category groups round-trip to a per-category target. A group
            // spanning several categories has no per-category equivalent and is dropped; a null (tracked) target
            // becomes 0. Down is a rare escape hatch, not a lossless inverse.
            migrationBuilder.Sql(@"
                INSERT INTO budget_categories (vehicle_id, category, annual_budget, created_at, updated_at, source)
                SELECT g.vehicle_id, c.category, COALESCE(g.annual_budget, 0), g.created_at, g.updated_at, g.source
                FROM budget_groups g
                JOIN budget_group_categories c ON c.budget_group_id = g.id
                WHERE (SELECT COUNT(*) FROM budget_group_categories c2 WHERE c2.budget_group_id = g.id) = 1;");

            migrationBuilder.DropTable(
                name: "budget_group_categories");

            migrationBuilder.DropTable(
                name: "budget_groups");
        }
    }
}
