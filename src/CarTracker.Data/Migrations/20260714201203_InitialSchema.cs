using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CarTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_categories",
                columns: table => new
                {
                    name = table.Column<string>(type: "varchar(24)", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "garages",
                columns: table => new
                {
                    name = table.Column<string>(type: "varchar(80)", nullable: false),
                    contact = table.Column<string>(type: "varchar(120)", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_garages", x => x.name);
                    table.CheckConstraint("ck_garages_notes", "notes <> ''");
                });

            migrationBuilder.CreateTable(
                name: "wash_locations",
                columns: table => new
                {
                    name = table.Column<string>(type: "varchar(80)", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wash_locations", x => x.name);
                    table.CheckConstraint("ck_wash_locations_notes", "notes <> ''");
                });

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    registration = table.Column<string>(type: "varchar(16)", nullable: false),
                    make = table.Column<string>(type: "varchar(40)", nullable: false),
                    model = table.Column<string>(type: "varchar(60)", nullable: false),
                    variant = table.Column<string>(type: "varchar(40)", nullable: true),
                    year = table.Column<int>(type: "integer", nullable: false),
                    colour = table.Column<string>(type: "varchar(30)", nullable: true),
                    body_style = table.Column<string>(type: "varchar(30)", nullable: true),
                    vin = table.Column<string>(type: "varchar(17)", nullable: true),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    seller = table.Column<string>(type: "varchar(120)", nullable: true),
                    purchase_price = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    purchase_mileage = table.Column<int>(type: "integer", nullable: false),
                    engine_code = table.Column<string>(type: "varchar(30)", nullable: true),
                    engine_size_cc = table.Column<int>(type: "integer", nullable: true),
                    fuel_type = table.Column<string>(type: "varchar(12)", nullable: false),
                    transmission = table.Column<string>(type: "varchar(30)", nullable: true),
                    drivetrain = table.Column<string>(type: "varchar(30)", nullable: true),
                    status = table.Column<string>(type: "varchar(6)", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    mot_expiry_seed = table.Column<DateOnly>(type: "date", nullable: true),
                    ved_annual_cost = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    ved_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    ulez_compliant = table.Column<bool>(type: "boolean", nullable: true),
                    oil_spec = table.Column<string>(type: "varchar(60)", nullable: true),
                    oil_capacity_litres = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    coolant_spec = table.Column<string>(type: "varchar(60)", nullable: true),
                    coolant_capacity_litres = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    brake_fluid_spec = table.Column<string>(type: "varchar(40)", nullable: true),
                    transmission_oil_spec = table.Column<string>(type: "varchar(60)", nullable: true),
                    spark_plug_part = table.Column<string>(type: "varchar(40)", nullable: true),
                    oil_filter_part = table.Column<string>(type: "varchar(40)", nullable: true),
                    air_filter_part = table.Column<string>(type: "varchar(40)", nullable: true),
                    fuel_filter_part = table.Column<string>(type: "varchar(40)", nullable: true),
                    cabin_filter_part = table.Column<string>(type: "varchar(40)", nullable: true),
                    tyre_size = table.Column<string>(type: "varchar(24)", nullable: true),
                    pressure_front_psi = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    pressure_rear_psi = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    pressure_front_laden_psi = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    pressure_rear_laden_psi = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    min_tread_mm = table.Column<decimal>(type: "numeric(3,1)", nullable: true),
                    insurance_insurer = table.Column<string>(type: "varchar(80)", nullable: true),
                    insurance_policy_number = table.Column<string>(type: "varchar(60)", nullable: true),
                    insurance_period_start = table.Column<DateOnly>(type: "date", nullable: true),
                    insurance_period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    insurance_cover_type = table.Column<string>(type: "varchar(40)", nullable: true),
                    insurance_premium = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    insurance_excess_compulsory = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    insurance_excess_voluntary = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    insurance_ncb_years = table.Column<int>(type: "integer", nullable: true),
                    breakdown_provider = table.Column<string>(type: "varchar(80)", nullable: true),
                    breakdown_policy_number = table.Column<string>(type: "varchar(60)", nullable: true),
                    breakdown_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    default_garage = table.Column<string>(type: "varchar(80)", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false),
                    registration_normalized = table.Column<string>(type: "varchar(16)", nullable: true, computedColumnSql: "upper(replace(registration, ' ', ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicles", x => x.id);
                    table.CheckConstraint("ck_vehicles_fuel_type", "fuel_type IN ('Petrol', 'Diesel', 'Hybrid', 'Electric', 'LPG')");
                    table.CheckConstraint("ck_vehicles_notes", "notes <> ''");
                    table.CheckConstraint("ck_vehicles_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.CheckConstraint("ck_vehicles_status", "status IN ('Active', 'Sold', 'SORN')");
                    table.ForeignKey(
                        name: "fk_vehicles_garages_default_garage",
                        column: x => x.default_garage,
                        principalTable: "garages",
                        principalColumn: "name",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "budget_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "varchar(24)", nullable: false),
                    annual_budget = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_categories", x => x.id);
                    table.CheckConstraint("ck_budget_categories_annual_budget", "annual_budget >= 0");
                    table.CheckConstraint("ck_budget_categories_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_budget_categories_expense_categories_category",
                        column: x => x.category,
                        principalTable: "expense_categories",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_budget_categories_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "check_definitions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "varchar(80)", nullable: false),
                    cadence_label = table.Column<string>(type: "varchar(40)", nullable: false),
                    interval_days = table.Column<int>(type: "integer", nullable: false),
                    guidance = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_definitions", x => x.id);
                    table.CheckConstraint("ck_check_definitions_interval", "interval_days > 0");
                    table.CheckConstraint("ck_check_definitions_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_check_definitions_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "equipment_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "varchar(120)", nullable: false),
                    category = table.Column<string>(type: "varchar(60)", nullable: true),
                    purchased_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source_vendor = table.Column<string>(type: "varchar(120)", nullable: true),
                    cost = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    stored_at = table.Column<string>(type: "varchar(120)", nullable: true),
                    status = table.Column<string>(type: "varchar(10)", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipment_items", x => x.id);
                    table.CheckConstraint("ck_equipment_items_notes", "notes <> ''");
                    table.CheckConstraint("ck_equipment_items_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.CheckConstraint("ck_equipment_items_status", "status IN ('Owned', 'OnOrder', 'ToOrder')");
                    table.ForeignKey(
                        name: "fk_equipment_items_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fuel_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    mileage = table.Column<int>(type: "integer", nullable: false),
                    litres = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    price_per_litre = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    station = table.Column<string>(type: "varchar(80)", nullable: true),
                    fill_level = table.Column<string>(type: "varchar(8)", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fuel_entries", x => x.id);
                    table.CheckConstraint("ck_fuel_entries_fill_level", "fill_level IN ('Full', 'Half', 'Quarter')");
                    table.CheckConstraint("ck_fuel_entries_litres", "litres > 0");
                    table.CheckConstraint("ck_fuel_entries_mileage", "mileage >= 0");
                    table.CheckConstraint("ck_fuel_entries_notes", "notes <> ''");
                    table.CheckConstraint("ck_fuel_entries_price_per_litre", "price_per_litre > 0");
                    table.CheckConstraint("ck_fuel_entries_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.CheckConstraint("ck_fuel_entries_total_cost", "total_cost > 0");
                    table.ForeignKey(
                        name: "fk_fuel_entries_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issues",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "varchar(160)", nullable: false),
                    severity = table.Column<string>(type: "varchar(8)", nullable: false),
                    first_noted = table.Column<DateOnly>(type: "date", nullable: false),
                    last_checked = table.Column<DateOnly>(type: "date", nullable: true),
                    current_observation = table.Column<string>(type: "text", nullable: true),
                    action_if_worsens = table.Column<string>(type: "text", nullable: true),
                    estimated_fix_cost = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    status = table.Column<string>(type: "varchar(10)", nullable: false),
                    resolved_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issues", x => x.id);
                    table.CheckConstraint("ck_issues_notes", "notes <> ''");
                    table.CheckConstraint("ck_issues_resolved_date_iff_resolved", "(status = 'Resolved') = (resolved_date IS NOT NULL)");
                    table.CheckConstraint("ck_issues_severity", "severity IN ('Critical', 'Medium', 'Low')");
                    table.CheckConstraint("ck_issues_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.CheckConstraint("ck_issues_status", "status IN ('Monitoring', 'Resolved')");
                    table.ForeignKey(
                        name: "fk_issues_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mileage_readings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    reading_date = table.Column<DateOnly>(type: "date", nullable: false),
                    mileage = table.Column<int>(type: "integer", nullable: false),
                    origin = table.Column<string>(type: "varchar(10)", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mileage_readings", x => x.id);
                    table.CheckConstraint("ck_mileage_readings_mileage", "mileage >= 0");
                    table.CheckConstraint("ck_mileage_readings_notes", "notes <> ''");
                    table.CheckConstraint("ck_mileage_readings_origin", "origin IN ('manual', 'fuel', 'tyre', 'wash', 'service')");
                    table.CheckConstraint("ck_mileage_readings_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_mileage_readings_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    mileage = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "varchar(40)", nullable: false),
                    garage = table.Column<string>(type: "varchar(80)", nullable: true),
                    work_done = table.Column<string>(type: "text", nullable: true),
                    parts_replaced = table.Column<string>(type: "text", nullable: true),
                    cost = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    next_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    next_due_mileage = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_records", x => x.id);
                    table.CheckConstraint("ck_service_records_mileage", "mileage >= 0");
                    table.CheckConstraint("ck_service_records_next_due_mileage", "next_due_mileage >= 0");
                    table.CheckConstraint("ck_service_records_notes", "notes <> ''");
                    table.CheckConstraint("ck_service_records_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_service_records_garages_garage",
                        column: x => x.garage,
                        principalTable: "garages",
                        principalColumn: "name",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_service_records_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tyre_readings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    reading_date = table.Column<DateOnly>(type: "date", nullable: false),
                    mileage = table.Column<int>(type: "integer", nullable: true),
                    psi_front_left = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    psi_front_right = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    psi_rear_left = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    psi_rear_right = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    psi_spare = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    tread_front_left = table.Column<decimal>(type: "numeric(3,1)", nullable: true),
                    tread_front_right = table.Column<decimal>(type: "numeric(3,1)", nullable: true),
                    tread_rear_left = table.Column<decimal>(type: "numeric(3,1)", nullable: true),
                    tread_rear_right = table.Column<decimal>(type: "numeric(3,1)", nullable: true),
                    location = table.Column<string>(type: "varchar(80)", nullable: true),
                    tool = table.Column<string>(type: "varchar(60)", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tyre_readings", x => x.id);
                    table.CheckConstraint("ck_tyre_readings_mileage", "mileage >= 0");
                    table.CheckConstraint("ck_tyre_readings_notes", "notes <> ''");
                    table.CheckConstraint("ck_tyre_readings_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_tyre_readings_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wash_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    wash_date = table.Column<DateOnly>(type: "date", nullable: false),
                    location = table.Column<string>(type: "varchar(80)", nullable: true),
                    wash_type = table.Column<string>(type: "varchar(40)", nullable: true),
                    cost = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    mileage = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wash_entries", x => x.id);
                    table.CheckConstraint("ck_wash_entries_mileage", "mileage >= 0");
                    table.CheckConstraint("ck_wash_entries_notes", "notes <> ''");
                    table.CheckConstraint("ck_wash_entries_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_wash_entries_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wash_entries_wash_locations_location",
                        column: x => x.location,
                        principalTable: "wash_locations",
                        principalColumn: "name",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "check_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    check_definition_id = table.Column<int>(type: "integer", nullable: false),
                    performed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    result = table.Column<string>(type: "varchar(12)", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_logs", x => x.id);
                    table.CheckConstraint("ck_check_logs_notes", "notes <> ''");
                    table.CheckConstraint("ck_check_logs_result", "result IN ('OK', 'Attention', 'Failed')");
                    table.CheckConstraint("ck_check_logs_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_check_logs_check_definitions_check_definition_id",
                        column: x => x.check_definition_id,
                        principalTable: "check_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expense_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    category = table.Column<string>(type: "varchar(24)", nullable: false),
                    sub_category = table.Column<string>(type: "varchar(60)", nullable: true),
                    vendor = table.Column<string>(type: "varchar(120)", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    mileage = table.Column<int>(type: "integer", nullable: true),
                    payment_method = table.Column<string>(type: "varchar(30)", nullable: true),
                    fuel_entry_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_entries", x => x.id);
                    table.CheckConstraint("ck_expense_entries_mileage", "mileage >= 0");
                    table.CheckConstraint("ck_expense_entries_notes", "notes <> ''");
                    table.CheckConstraint("ck_expense_entries_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.ForeignKey(
                        name: "fk_expense_entries_expense_categories_category",
                        column: x => x.category,
                        principalTable: "expense_categories",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_entries_fuel_entries_fuel_entry_id",
                        column: x => x.fuel_entry_id,
                        principalTable: "fuel_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expense_entries_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_tasks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "varchar(8)", nullable: false),
                    priority = table.Column<string>(type: "varchar(6)", nullable: false),
                    title = table.Column<string>(type: "varchar(160)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    estimated_cost = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    status = table.Column<string>(type: "varchar(12)", nullable: false),
                    target_date = table.Column<DateOnly>(type: "date", nullable: true),
                    target_service = table.Column<string>(type: "varchar(80)", nullable: true),
                    completed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    assigned_garage = table.Column<string>(type: "varchar(80)", nullable: true),
                    service_record_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_tasks", x => x.id);
                    table.CheckConstraint("ck_maintenance_tasks_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.CheckConstraint("ck_tasks_completed_date_iff_done", "(status = 'Done') = (completed_date IS NOT NULL)");
                    table.CheckConstraint("ck_tasks_garage_workshop_only", "assigned_garage IS NULL OR kind = 'Workshop'");
                    table.CheckConstraint("ck_tasks_kind", "kind IN ('DIY', 'Workshop')");
                    table.CheckConstraint("ck_tasks_notes", "notes <> ''");
                    table.CheckConstraint("ck_tasks_priority", "priority IN ('High', 'Medium', 'Low')");
                    table.CheckConstraint("ck_tasks_status", "status IN ('Open', 'InProgress', 'Scheduled', 'Done')");
                    table.ForeignKey(
                        name: "fk_maintenance_tasks_garages_assigned_garage",
                        column: x => x.assigned_garage,
                        principalTable: "garages",
                        principalColumn: "name",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_maintenance_tasks_service_records_service_record_id",
                        column: x => x.service_record_id,
                        principalTable: "service_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_maintenance_tasks_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "varchar(20)", nullable: false),
                    title = table.Column<string>(type: "varchar(160)", nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: true),
                    file_path = table.Column<string>(type: "varchar(400)", nullable: false),
                    content_type = table.Column<string>(type: "varchar(100)", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "char(64)", nullable: true),
                    service_record_id = table.Column<int>(type: "integer", nullable: true),
                    expense_entry_id = table.Column<int>(type: "integer", nullable: true),
                    issue_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source = table.Column<string>(type: "varchar(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.CheckConstraint("ck_documents_notes", "notes <> ''");
                    table.CheckConstraint("ck_documents_size_bytes", "size_bytes > 0");
                    table.CheckConstraint("ck_documents_source", "source IN ('web', 'mcp', 'import', 'seed')");
                    table.CheckConstraint("ck_documents_type", "type IN ('V5C', 'Insurance', 'MOT', 'Receipt', 'Photo', 'Manual', 'Other')");
                    table.ForeignKey(
                        name: "fk_documents_expense_entries_expense_entry_id",
                        column: x => x.expense_entry_id,
                        principalTable: "expense_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_documents_issues_issue_id",
                        column: x => x.issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_documents_service_records_service_record_id",
                        column: x => x.service_record_id,
                        principalTable: "service_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_documents_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "expense_categories",
                columns: new[] { "name", "display_order", "is_system" },
                values: new object[,]
                {
                    { "Breakdown", 11, true },
                    { "Fuel", 1, true },
                    { "Insurance", 5, true },
                    { "Misc", 13, true },
                    { "MOT", 7, true },
                    { "Parking", 9, true },
                    { "Parts", 4, true },
                    { "Purchase", 12, true },
                    { "Repair", 3, true },
                    { "Service", 2, true },
                    { "Tax", 6, true },
                    { "Tools/Equipment", 10, true },
                    { "Wash", 8, true }
                });

            migrationBuilder.CreateIndex(
                name: "ix_budget_categories_category",
                table: "budget_categories",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_budget_vehicle_category",
                table: "budget_categories",
                columns: new[] { "vehicle_id", "category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_check_definitions_vehicle_name",
                table: "check_definitions",
                columns: new[] { "vehicle_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_check_logs_definition_date",
                table: "check_logs",
                columns: new[] { "check_definition_id", "performed_on" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_documents_expense_entry_id",
                table: "documents",
                column: "expense_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_issue_id",
                table: "documents",
                column: "issue_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_service_record_id",
                table: "documents",
                column: "service_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_vehicle_type",
                table: "documents",
                columns: new[] { "vehicle_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_equipment_vehicle_status",
                table: "equipment_items",
                columns: new[] { "vehicle_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_entries_category",
                table: "expense_entries",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_expense_entries_fuel_entry_id",
                table: "expense_entries",
                column: "fuel_entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_entries_vehicle_category",
                table: "expense_entries",
                columns: new[] { "vehicle_id", "category" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_entries_vehicle_date",
                table: "expense_entries",
                columns: new[] { "vehicle_id", "entry_date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_fuel_entries_vehicle_date",
                table: "fuel_entries",
                columns: new[] { "vehicle_id", "entry_date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_fuel_entries_vehicle_mileage",
                table: "fuel_entries",
                columns: new[] { "vehicle_id", "mileage" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_issues_vehicle_status_severity",
                table: "issues",
                columns: new[] { "vehicle_id", "status", "severity" });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_tasks_assigned_garage",
                table: "maintenance_tasks",
                column: "assigned_garage");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_tasks_service_record_id",
                table: "maintenance_tasks",
                column: "service_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_vehicle_status",
                table: "maintenance_tasks",
                columns: new[] { "vehicle_id", "status", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_mileage_readings_vehicle_date",
                table: "mileage_readings",
                columns: new[] { "vehicle_id", "reading_date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_mileage_readings_vehicle_mileage",
                table: "mileage_readings",
                columns: new[] { "vehicle_id", "mileage" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_service_records_garage",
                table: "service_records",
                column: "garage");

            migrationBuilder.CreateIndex(
                name: "ix_service_records_type_next_due",
                table: "service_records",
                columns: new[] { "vehicle_id", "type", "next_due_date" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_service_records_vehicle_date",
                table: "service_records",
                columns: new[] { "vehicle_id", "service_date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_tyre_readings_vehicle_date",
                table: "tyre_readings",
                columns: new[] { "vehicle_id", "reading_date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_default",
                table: "vehicles",
                column: "is_default",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_default_garage",
                table: "vehicles",
                column: "default_garage");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_registration",
                table: "vehicles",
                column: "registration_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wash_entries_location",
                table: "wash_entries",
                column: "location");

            migrationBuilder.CreateIndex(
                name: "ix_wash_entries_vehicle_date",
                table: "wash_entries",
                columns: new[] { "vehicle_id", "wash_date" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_categories");

            migrationBuilder.DropTable(
                name: "check_logs");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "equipment_items");

            migrationBuilder.DropTable(
                name: "maintenance_tasks");

            migrationBuilder.DropTable(
                name: "mileage_readings");

            migrationBuilder.DropTable(
                name: "tyre_readings");

            migrationBuilder.DropTable(
                name: "wash_entries");

            migrationBuilder.DropTable(
                name: "check_definitions");

            migrationBuilder.DropTable(
                name: "expense_entries");

            migrationBuilder.DropTable(
                name: "issues");

            migrationBuilder.DropTable(
                name: "service_records");

            migrationBuilder.DropTable(
                name: "wash_locations");

            migrationBuilder.DropTable(
                name: "expense_categories");

            migrationBuilder.DropTable(
                name: "fuel_entries");

            migrationBuilder.DropTable(
                name: "vehicles");

            migrationBuilder.DropTable(
                name: "garages");
        }
    }
}
