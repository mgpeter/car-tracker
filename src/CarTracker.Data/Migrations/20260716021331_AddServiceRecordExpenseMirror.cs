using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRecordExpenseMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "service_record_id",
                table: "expense_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_entries_service_record_id",
                table: "expense_entries",
                column: "service_record_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_expense_entries_service_records_service_record_id",
                table: "expense_entries",
                column: "service_record_id",
                principalTable: "service_records",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_expense_entries_service_records_service_record_id",
                table: "expense_entries");

            migrationBuilder.DropIndex(
                name: "ix_expense_entries_service_record_id",
                table: "expense_entries");

            migrationBuilder.DropColumn(
                name: "service_record_id",
                table: "expense_entries");
        }
    }
}
