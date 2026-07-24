using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vehicles_default",
                table: "vehicles");

            migrationBuilder.DropIndex(
                name: "ix_vehicles_registration",
                table: "vehicles");

            migrationBuilder.AddColumn<int>(
                name: "owner_id",
                table: "vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "owner_id",
                table: "assistant_tokens",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "varchar(128)", nullable: false),
                    email = table.Column<string>(type: "varchar(320)", nullable: false),
                    display_name = table.Column<string>(type: "varchar(120)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_default",
                table: "vehicles",
                columns: new[] { "owner_id", "is_default" },
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_registration",
                table: "vehicles",
                columns: new[] { "owner_id", "registration_normalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assistant_tokens_owner_id",
                table: "assistant_tokens",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_external_id",
                table: "users",
                column: "external_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_assistant_tokens_users_owner_id",
                table: "assistant_tokens",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_vehicles_users_owner_id",
                table: "vehicles",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assistant_tokens_users_owner_id",
                table: "assistant_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_vehicles_users_owner_id",
                table: "vehicles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropIndex(
                name: "ix_vehicles_default",
                table: "vehicles");

            migrationBuilder.DropIndex(
                name: "ix_vehicles_registration",
                table: "vehicles");

            migrationBuilder.DropIndex(
                name: "ix_assistant_tokens_owner_id",
                table: "assistant_tokens");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "assistant_tokens");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_default",
                table: "vehicles",
                column: "is_default",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_registration",
                table: "vehicles",
                column: "registration_normalized",
                unique: true);
        }
    }
}
