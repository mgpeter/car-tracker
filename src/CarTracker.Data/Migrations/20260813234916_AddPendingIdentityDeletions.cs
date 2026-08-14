using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <summary>
    /// The queue that makes an identity deletion a promise rather than a best effort.
    /// </summary>
    /// <remarks>
    /// Scaffolded and kept as generated — unlike <c>AddPerOwnerReferenceLists</c>, this one adds a table and
    /// touches nothing, so there is no ordering to get right and <c>Down()</c> is honest: dropping it loses only
    /// outstanding retries, and the local data those rows refer to is already gone either way.
    /// </remarks>
    public partial class AddPendingIdentityDeletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pending_identity_deletions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "varchar(128)", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_identity_deletions", x => x.id);
                    table.CheckConstraint("ck_pending_identity_deletions_last_error", "last_error <> ''");
                });

            migrationBuilder.CreateIndex(
                name: "ix_pending_identity_deletions_external_id",
                table: "pending_identity_deletions",
                column: "external_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_identity_deletions");
        }
    }
}
