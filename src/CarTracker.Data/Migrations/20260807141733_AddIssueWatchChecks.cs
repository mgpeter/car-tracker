using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueWatchChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_watch_checks",
                columns: table => new
                {
                    issue_id = table.Column<int>(type: "integer", nullable: false),
                    check_definition_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_watch_checks", x => new { x.issue_id, x.check_definition_id });
                    table.ForeignKey(
                        name: "fk_issue_watch_checks_check_definitions_check_definition_id",
                        column: x => x.check_definition_id,
                        principalTable: "check_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_watch_checks_issues_issue_id",
                        column: x => x.issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issue_watch_checks_check_definition_id",
                table: "issue_watch_checks",
                column: "check_definition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_watch_checks");
        }
    }
}
