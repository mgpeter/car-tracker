using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentExpenseMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "equipment_item_id",
                table: "expense_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_entries_equipment_item_id",
                table: "expense_entries",
                column: "equipment_item_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_expense_entries_equipment_items_equipment_item_id",
                table: "expense_entries",
                column: "equipment_item_id",
                principalTable: "equipment_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_expense_entries_equipment_items_equipment_item_id",
                table: "expense_entries");

            migrationBuilder.DropIndex(
                name: "ix_expense_entries_equipment_item_id",
                table: "expense_entries");

            migrationBuilder.DropColumn(
                name: "equipment_item_id",
                table: "expense_entries");
        }
    }
}
