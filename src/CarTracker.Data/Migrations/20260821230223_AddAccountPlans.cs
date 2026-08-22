using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "email_verified",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every existing account was verified, and this asserts it rather than assuming it. Until this
            // release the only door was DEC-018's invitation list, and SignupPolicy.Admits refused an
            // unverified address outright - so a row that exists at all was provisioned against an address the
            // tenant had confirmed. The exception is a row still carrying the pre-Management fallback, where
            // `email = external_id` and no address was ever resolved; those are left false and repaired by
            // AccountProvisioner.BackfillEmailAsync on their next request.
            //
            // Without this, the release is not a no-op for anybody: every existing account lands on the free
            // tier and loses the assistant until it happens to sign in again against a configured Management
            // credential - and on a deployment that has none, permanently.
            migrationBuilder.Sql("UPDATE users SET email_verified = TRUE WHERE email <> external_id;");

            migrationBuilder.CreateTable(
                name: "vehicle_lookup_usage",
                columns: table => new
                {
                    owner_id = table.Column<int>(type: "integer", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    lookups = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_lookup_usage", x => new { x.owner_id, x.day });
                    table.CheckConstraint("ck_vehicle_lookup_usage_non_negative", "lookups >= 0");
                    table.ForeignKey(
                        name: "fk_vehicle_lookup_usage_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicle_lookup_usage");

            migrationBuilder.DropColumn(
                name: "email_verified",
                table: "users");
        }
    }
}
