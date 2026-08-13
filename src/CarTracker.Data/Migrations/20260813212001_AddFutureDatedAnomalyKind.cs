using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFutureDatedAnomalyKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_anomalies_kind",
                table: "data_anomalies");

            migrationBuilder.AddCheckConstraint(
                name: "ck_anomalies_kind",
                table: "data_anomalies",
                sql: "kind IN ('MileageNonMonotonic', 'FuelCostDiscrepancy', 'ImplausibleMpg', 'EquipmentCostWithoutDate', 'FutureDatedEntry')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_anomalies_kind",
                table: "data_anomalies");

            // Flags of the new kind must go before the narrower constraint is restored, or re-adding it fails
            // on its own data. They are derived — the next scan re-raises any that are still true.
            migrationBuilder.Sql("DELETE FROM data_anomalies WHERE kind = 'FutureDatedEntry';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_anomalies_kind",
                table: "data_anomalies",
                sql: "kind IN ('MileageNonMonotonic', 'FuelCostDiscrepancy', 'ImplausibleMpg', 'EquipmentCostWithoutDate')");
        }
    }
}
