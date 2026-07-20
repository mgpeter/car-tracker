using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "varchar(80)", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                    scope = table.Column<string>(type: "varchar(10)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    read_count = table.Column<int>(type: "integer", nullable: false),
                    write_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_tokens", x => x.id);
                    table.CheckConstraint("ck_assistant_tokens_scope", "scope IN ('ReadOnly', 'ReadWrite')");
                });

            migrationBuilder.CreateTable(
                name: "assistant_write_audits",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    token_id = table.Column<int>(type: "integer", nullable: false),
                    tool = table.Column<string>(type: "varchar(60)", nullable: false),
                    vehicle_id = table.Column<int>(type: "integer", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: false),
                    timestamp_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_write_audits", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_write_audits_assistant_tokens_token_id",
                        column: x => x.token_id,
                        principalTable: "assistant_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_tokens_hash",
                table: "assistant_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assistant_write_audits_time",
                table: "assistant_write_audits",
                column: "timestamp_utc");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_write_audits_token_id",
                table: "assistant_write_audits",
                column: "token_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_write_audits");

            migrationBuilder.DropTable(
                name: "assistant_tokens");
        }
    }
}
