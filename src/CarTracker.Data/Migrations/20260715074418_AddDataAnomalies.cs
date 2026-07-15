using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDataAnomalies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_anomalies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "varchar(24)", nullable: false),
                    severity = table.Column<string>(type: "varchar(8)", nullable: false),
                    entity_type = table.Column<string>(type: "varchar(40)", nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: true),
                    message = table.Column<string>(type: "text", nullable: false),
                    detail = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "varchar(10)", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    resolution_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_anomalies", x => x.id);
                    table.CheckConstraint("ck_anomalies_kind", "kind IN ('MileageNonMonotonic', 'FuelCostDiscrepancy', 'ImplausibleMpg')");
                    table.CheckConstraint("ck_anomalies_resolution_note", "resolution_note <> ''");
                    table.CheckConstraint("ck_anomalies_resolved_iff_terminal", "(status = 'Open') = (resolved_at IS NULL)");
                    table.CheckConstraint("ck_anomalies_severity", "severity IN ('Error', 'Warning', 'Info')");
                    table.CheckConstraint("ck_anomalies_status", "status IN ('Open', 'Accepted', 'Corrected', 'Dismissed')");
                    table.CheckConstraint("ck_data_anomalies_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_data_anomalies_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anomalies_entity",
                table: "data_anomalies",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_anomalies_vehicle_status",
                table: "data_anomalies",
                columns: new[] { "vehicle_id", "status", "severity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_anomalies");
        }
    }
}
