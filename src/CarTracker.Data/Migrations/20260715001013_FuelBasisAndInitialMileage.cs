using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class FuelBasisAndInitialMileage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_mileage_readings_origin",
                table: "mileage_readings");

            migrationBuilder.AlterColumn<string>(
                name: "fill_level",
                table: "fuel_entries",
                type: "varchar(8)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(8)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_mileage_readings_origin",
                table: "mileage_readings",
                sql: "origin IN ('purchase', 'manual', 'fuel', 'tyre', 'wash', 'service')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_mileage_readings_origin",
                table: "mileage_readings");

            migrationBuilder.AlterColumn<string>(
                name: "fill_level",
                table: "fuel_entries",
                type: "varchar(8)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(8)",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_mileage_readings_origin",
                table: "mileage_readings",
                sql: "origin IN ('manual', 'fuel', 'tyre', 'wash', 'service')");
        }
    }
}
