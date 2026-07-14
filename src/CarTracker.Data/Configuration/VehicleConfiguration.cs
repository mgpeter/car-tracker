using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles", t =>
        {
            t.HasCheckConstraint("ck_vehicles_status", "status IN ('Active', 'Sold', 'SORN')");
            t.HasCheckConstraint("ck_vehicles_fuel_type", "fuel_type IN ('Petrol', 'Diesel', 'Hybrid', 'Electric', 'LPG')");
            t.HasCheckConstraint("ck_vehicles_notes", "notes <> ''");
        });

        builder.HasKey(v => v.Id);

        // Identity. The stored generated column normalises case and spacing so BT53AKJ and "bt53 akj"
        // cannot coexist — EF cannot model an expression index, and a generated column is equivalent.
        builder.Property(v => v.Registration).HasColumnType("varchar(16)").IsRequired();
        builder.Property<string>("RegistrationNormalized")
            .HasColumnType("varchar(16)")
            .HasComputedColumnSql("upper(replace(registration, ' ', ''))", stored: true);
        builder.HasIndex("RegistrationNormalized").IsUnique().HasDatabaseName("ix_vehicles_registration");

        builder.Property(v => v.Make).HasColumnType("varchar(40)").IsRequired();
        builder.Property(v => v.Model).HasColumnType("varchar(60)").IsRequired();
        builder.Property(v => v.Variant).HasColumnType("varchar(40)");
        builder.Property(v => v.Year).HasColumnType("integer").IsRequired();
        builder.Property(v => v.Colour).HasColumnType("varchar(30)");
        builder.Property(v => v.BodyStyle).HasColumnType("varchar(30)");
        builder.Property(v => v.Vin).HasColumnType("varchar(17)");

        // Purchase
        builder.Property(v => v.PurchaseDate).HasColumnType("date").IsRequired();
        builder.Property(v => v.Seller).HasColumnType("varchar(120)");
        builder.Property(v => v.PurchasePrice).HasColumnType("numeric(10,2)");
        builder.Property(v => v.PurchaseMileage).HasColumnType("integer").IsRequired();

        // Engine / drivetrain
        builder.Property(v => v.EngineCode).HasColumnType("varchar(30)");
        builder.Property(v => v.EngineSizeCc).HasColumnType("integer");
        builder.Property(v => v.FuelType).HasColumnType("varchar(12)").HasConversion<string>().IsRequired();
        builder.Property(v => v.Transmission).HasColumnType("varchar(30)");
        builder.Property(v => v.Drivetrain).HasColumnType("varchar(30)");

        // Lifecycle (DEC-007). The partial unique index allows at most one default vehicle; zero is legal.
        builder.Property(v => v.Status).HasColumnType("varchar(6)").HasConversion<string>().IsRequired();
        builder.Property(v => v.IsDefault).HasColumnType("boolean").IsRequired();
        builder.HasIndex(v => v.IsDefault)
            .IsUnique()
            .HasFilter("is_default")
            .HasDatabaseName("ix_vehicles_default");

        // Statutory. mot_expiry_seed is deliberately not named mot_expiry — see the schema spec.
        builder.Property(v => v.MotExpirySeed).HasColumnType("date");
        builder.Property(v => v.VedAnnualCost).HasColumnType("numeric(10,2)");
        builder.Property(v => v.VedExpiry).HasColumnType("date");
        builder.Property(v => v.UlezCompliant).HasColumnType("boolean");

        // Owned blocks. Column names are explicit so the schema's flat names survive the owned-type
        // prefixing the naming convention would otherwise apply.
        builder.OwnsOne(v => v.Fluids, fluids =>
        {
            fluids.Property(f => f.OilSpec).HasColumnType("varchar(60)").HasColumnName("oil_spec");
            fluids.Property(f => f.OilCapacityLitres).HasColumnType("numeric(4,2)").HasColumnName("oil_capacity_litres");
            fluids.Property(f => f.CoolantSpec).HasColumnType("varchar(60)").HasColumnName("coolant_spec");
            fluids.Property(f => f.CoolantCapacityLitres).HasColumnType("numeric(4,2)").HasColumnName("coolant_capacity_litres");
            fluids.Property(f => f.BrakeFluidSpec).HasColumnType("varchar(40)").HasColumnName("brake_fluid_spec");
            fluids.Property(f => f.TransmissionOilSpec).HasColumnType("varchar(60)").HasColumnName("transmission_oil_spec");
            fluids.Property(f => f.SparkPlugPart).HasColumnType("varchar(40)").HasColumnName("spark_plug_part");
            fluids.Property(f => f.OilFilterPart).HasColumnType("varchar(40)").HasColumnName("oil_filter_part");
            fluids.Property(f => f.AirFilterPart).HasColumnType("varchar(40)").HasColumnName("air_filter_part");
            fluids.Property(f => f.FuelFilterPart).HasColumnType("varchar(40)").HasColumnName("fuel_filter_part");
            fluids.Property(f => f.CabinFilterPart).HasColumnType("varchar(40)").HasColumnName("cabin_filter_part");
        });

        builder.OwnsOne(v => v.Tyres, tyres =>
        {
            tyres.Property(t => t.TyreSize).HasColumnType("varchar(24)").HasColumnName("tyre_size");
            tyres.Property(t => t.PressureFrontPsi).HasColumnType("numeric(4,1)").HasColumnName("pressure_front_psi");
            tyres.Property(t => t.PressureRearPsi).HasColumnType("numeric(4,1)").HasColumnName("pressure_rear_psi");
            tyres.Property(t => t.PressureFrontLadenPsi).HasColumnType("numeric(4,1)").HasColumnName("pressure_front_laden_psi");
            tyres.Property(t => t.PressureRearLadenPsi).HasColumnType("numeric(4,1)").HasColumnName("pressure_rear_laden_psi");
            tyres.Property(t => t.MinTreadMm).HasColumnType("numeric(3,1)").HasColumnName("min_tread_mm");
        });

        builder.OwnsOne(v => v.Insurance, insurance =>
        {
            insurance.Property(i => i.Insurer).HasColumnType("varchar(80)").HasColumnName("insurance_insurer");
            insurance.Property(i => i.PolicyNumber).HasColumnType("varchar(60)").HasColumnName("insurance_policy_number");
            insurance.Property(i => i.PeriodStart).HasColumnType("date").HasColumnName("insurance_period_start");
            insurance.Property(i => i.PeriodEnd).HasColumnType("date").HasColumnName("insurance_period_end");
            insurance.Property(i => i.CoverType).HasColumnType("varchar(40)").HasColumnName("insurance_cover_type");
            insurance.Property(i => i.Premium).HasColumnType("numeric(10,2)").HasColumnName("insurance_premium");
            insurance.Property(i => i.ExcessCompulsory).HasColumnType("numeric(10,2)").HasColumnName("insurance_excess_compulsory");
            insurance.Property(i => i.ExcessVoluntary).HasColumnType("numeric(10,2)").HasColumnName("insurance_excess_voluntary");
            insurance.Property(i => i.NcbYears).HasColumnType("integer").HasColumnName("insurance_ncb_years");
        });

        builder.OwnsOne(v => v.Breakdown, breakdown =>
        {
            breakdown.Property(b => b.Provider).HasColumnType("varchar(80)").HasColumnName("breakdown_provider");
            breakdown.Property(b => b.PolicyNumber).HasColumnType("varchar(60)").HasColumnName("breakdown_policy_number");
            breakdown.Property(b => b.Expiry).HasColumnType("date").HasColumnName("breakdown_expiry");
        });

        builder.Property(v => v.DefaultGarage).HasColumnType("varchar(80)");
        builder.HasOne<Garage>()
            .WithMany()
            .HasForeignKey(v => v.DefaultGarage)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(v => v.Notes).HasColumnType("text");

        builder.ConfigureAudit("vehicles");
    }
}
