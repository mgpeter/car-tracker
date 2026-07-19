using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// Static reference per README §2. Insurance, breakdown, fluid and tyre blocks are EF owned types mapped to
/// columns on this table — they are 1:1 with the vehicle and have no independent lifetime.
/// </summary>
public class Vehicle : IAuditable
{
    public int Id { get; set; }

    // Identity
    public required string Registration { get; set; }
    public required string Make { get; set; }
    public required string Model { get; set; }
    public string? Variant { get; set; }
    public int Year { get; set; }
    public string? Colour { get; set; }
    public string? BodyStyle { get; set; }
    public string? Vin { get; set; }

    // Purchase
    public DateOnly PurchaseDate { get; set; }
    public string? Seller { get; set; }
    public decimal? PurchasePrice { get; set; }
    public int PurchaseMileage { get; set; }

    // Engine / drivetrain
    public string? EngineCode { get; set; }
    public int? EngineSizeCc { get; set; }
    public FuelType FuelType { get; set; }
    public string? Transmission { get; set; }
    public string? Drivetrain { get; set; }

    // Lifecycle (DEC-007)
    public VehicleStatus Status { get; set; } = VehicleStatus.Active;
    public bool IsDefault { get; set; }

    // Statutory. MotExpirySeed is NOT the MOT expiry — derived expiry comes from the latest MOT service
    // record; this is read only when no such record exists yet.
    public DateOnly? MotExpirySeed { get; set; }
    public decimal? VedAnnualCost { get; set; }
    public DateOnly? VedExpiry { get; set; }
    public bool? UlezCompliant { get; set; }

    // Owned blocks
    public FluidSpecs Fluids { get; set; } = new();
    public TyreSpecs Tyres { get; set; } = new();
    public InsurancePolicy Insurance { get; set; } = new();
    public BreakdownCover Breakdown { get; set; } = new();

    public string? DefaultGarage { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}

/// <summary>Fluids, capacities and consumable part numbers — the "at the pump" reference block.</summary>
public class FluidSpecs
{
    public string? OilSpec { get; set; }
    public decimal? OilCapacityLitres { get; set; }
    public string? CoolantSpec { get; set; }
    public decimal? CoolantCapacityLitres { get; set; }

    /// <summary>
    /// Usable fuel-tank capacity in litres. Nullable and never defaulted: it feeds the derived full-tank range,
    /// and the app shows nothing rather than a guessed size — the same restraint as a null MilesPerDay.
    /// </summary>
    public decimal? FuelTankCapacityLitres { get; set; }
    public string? BrakeFluidSpec { get; set; }
    public string? TransmissionOilSpec { get; set; }
    public string? SparkPlugPart { get; set; }
    public string? OilFilterPart { get; set; }
    public string? AirFilterPart { get; set; }
    public string? FuelFilterPart { get; set; }
    public string? CabinFilterPart { get; set; }
}

public class TyreSpecs
{
    public string? TyreSize { get; set; }
    public decimal? PressureFrontPsi { get; set; }
    public decimal? PressureRearPsi { get; set; }
    public decimal? PressureFrontLadenPsi { get; set; }
    public decimal? PressureRearLadenPsi { get; set; }
    public decimal? MinTreadMm { get; set; }
}

public class InsurancePolicy
{
    public string? Insurer { get; set; }
    public string? PolicyNumber { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public string? CoverType { get; set; }
    public decimal? Premium { get; set; }
    public decimal? ExcessCompulsory { get; set; }
    public decimal? ExcessVoluntary { get; set; }
    public int? NcbYears { get; set; }
}

public class BreakdownCover
{
    public string? Provider { get; set; }
    public string? PolicyNumber { get; set; }
    public DateOnly? Expiry { get; set; }
}
