using CarTracker.Domain;
using CarTracker.Domain.Vehicles;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using CarTracker.Shared.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The shared vehicle-settings write path — the code behind the web PATCH and the MCP <c>set_insurance</c> /
/// <c>set_road_tax</c> / <c>update_vehicle_profile</c> tools. Setting insurance and VED dates is what makes the
/// renewals fire; the point of the whole change is that they come straight back on the recomputed summary.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class VehicleUpdateServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private int _ownerId;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _clock);

    private VehicleUpdateService NewService(CarTrackerDbContext context) =>
        new(context, new DerivedMetricsService(new VehicleMetricsLoader(context), new Clock(_clock)),
            new ReferenceWriter(context, TestOwner.As(_ownerId)), new VehiclePurchaseMirror(context));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_vehicleupdate");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> SeedVehicleAsync(string registration)
    {
        await using var context = NewContext();
        var vehicle = await new VehicleFactory(context).CreateAsync(
            new Vehicle
            {
                Registration = registration,
                Make = "Land Rover",
                Model = "Freelander",
                Year = 2003,
                PurchaseDate = new DateOnly(2026, 3, 14),
                PurchaseMileage = 76_632,
                FuelType = FuelType.Petrol,
                Source = EntrySource.Web,
            },
            _ownerId,
            EntrySource.Web);
        return vehicle.Id;
    }

    [Fact]
    public async Task Setting_insurance_dates_drives_the_insurance_renewal()
    {
        var vehicleId = await SeedVehicleAsync("VUP1 AAA");

        await using var context = NewContext();
        var result = await NewService(context).ApplyAsync(
            vehicleId,
            new VehiclePatch(Insurance: new InsurancePatch(
                Insurer: "Admiral", CoverType: "Comprehensive",
                PeriodStart: new DateOnly(2026, 2, 1), PeriodEnd: new DateOnly(2027, 1, 31))));

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.NotNull(result.Value);
        // The renewal that was a blind spot is now derived from the date just set.
        Assert.Equal(new DateOnly(2027, 1, 31), result.Value!.Renewals.Insurance.ExpiryDate);
        Assert.NotNull(result.Value.Renewals.Insurance.DaysRemaining);
    }

    [Fact]
    public async Task Setting_ved_expiry_drives_the_road_tax_renewal()
    {
        var vehicleId = await SeedVehicleAsync("VUP2 BBB");

        await using var context = NewContext();
        var result = await NewService(context).ApplyAsync(
            vehicleId, new VehiclePatch(VedExpiry: new DateOnly(2027, 1, 31), VedAnnualCost: 180m));

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.Equal(new DateOnly(2027, 1, 31), result.Value!.Renewals.RoadTax.ExpiryDate);
    }

    [Fact]
    public async Task A_policy_cannot_end_before_it_starts()
    {
        var vehicleId = await SeedVehicleAsync("VUP3 CCC");

        await using var context = NewContext();
        var result = await NewService(context).ApplyAsync(
            vehicleId,
            new VehiclePatch(Insurance: new InsurancePatch(
                PeriodStart: new DateOnly(2027, 1, 31), PeriodEnd: new DateOnly(2026, 2, 1))));

        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.True(result.Errors!.ContainsKey("Insurance.PeriodEnd"));
    }

    [Fact]
    public async Task Setting_a_new_default_garage_creates_the_keyed_garage_row()
    {
        var vehicleId = await SeedVehicleAsync("VUP5 EEE");

        await using (var context = NewContext())
        {
            // The garage FK looks like free text but points at a keyed table; without the upsert this is an FK 500.
            var result = await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(DefaultGarage: "K & P Motors"));
            Assert.Equal(WriteStatus.Updated, result.Status);
        }

        await using (var reader = NewContext())
        {
            Assert.True(await reader.Garages.AnyAsync(g => g.Name == "K & P Motors"));
            Assert.Equal("K & P Motors", (await reader.Vehicles.SingleAsync(v => v.Id == vehicleId)).DefaultGarage);
        }
    }

    [Fact]
    public async Task An_over_length_cover_type_is_rejected_with_a_clear_message()
    {
        var vehicleId = await SeedVehicleAsync("VUP6 FFF");

        await using var context = NewContext();
        var result = await NewService(context).ApplyAsync(
            vehicleId, new VehiclePatch(Insurance: new InsurancePatch(CoverType: new string('x', 41))));

        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.True(result.Errors!.ContainsKey("coverType"));
    }

    [Fact]
    public async Task Fluid_and_tyre_specs_round_trip_and_merge_per_field()
    {
        var vehicleId = await SeedVehicleAsync("VUP7 GGG");

        await using (var context = NewContext())
        {
            await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(
                Fluids: new FluidsPatch(OilSpec: "5W-30 A3/B4", CoolantSpec: "OAT (red)", OilFilterPart: "W712/75"),
                Tyres: new TyresPatch(TyreSize: "215/65 R16", PressureRearLadenPsi: 38m, MinTreadMm: 1.6m)));
        }

        // A second patch touching only one fluid must not wipe the others (per-field merge).
        await using (var context = NewContext())
        {
            await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(Fluids: new FluidsPatch(BrakeFluidSpec: "DOT 4")));
        }

        await using (var reader = NewContext())
        {
            var v = await reader.Vehicles.SingleAsync(x => x.Id == vehicleId);
            Assert.Equal("5W-30 A3/B4", v.Fluids.OilSpec);
            Assert.Equal("OAT (red)", v.Fluids.CoolantSpec);   // the K-series head-gasket warning now has a home
            Assert.Equal("W712/75", v.Fluids.OilFilterPart);
            Assert.Equal("DOT 4", v.Fluids.BrakeFluidSpec);
            Assert.Equal("215/65 R16", v.Tyres.TyreSize);
            Assert.Equal(38m, v.Tyres.PressureRearLadenPsi);
            Assert.Equal(1.6m, v.Tyres.MinTreadMm);
        }
    }

    /// <summary>
    /// Breakdown cover was on the entity and in the detail response from Phase 1 and had no patch, so nothing
    /// could ever write it. The merge follows insurance exactly: per field, a null leaves the stored value.
    /// </summary>
    [Fact]
    public async Task Breakdown_cover_round_trips_and_merges_per_field()
    {
        var vehicleId = await SeedVehicleAsync("VUP8 HHH");

        await using (var context = NewContext())
        {
            var result = await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(
                Breakdown: new BreakdownPatch(
                    Provider: "Green Flag", PolicyNumber: "GF-88213", Expiry: new DateOnly(2027, 3, 1))));
            Assert.Equal(WriteStatus.Updated, result.Status);
        }

        // Renewing the cover moves the date and leaves the provider and policy number alone.
        await using (var context = NewContext())
        {
            await NewService(context).ApplyAsync(
                vehicleId, new VehiclePatch(Breakdown: new BreakdownPatch(Expiry: new DateOnly(2028, 3, 1))));
        }

        // And a patch with no Breakdown block at all must not touch it - the trap FuelTankCapacity fell into.
        await using (var context = NewContext())
        {
            await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(Colour: "Epsom Green"));
        }

        await using (var reader = NewContext())
        {
            var v = await reader.Vehicles.SingleAsync(x => x.Id == vehicleId);
            Assert.Equal("Green Flag", v.Breakdown.Provider);
            Assert.Equal("GF-88213", v.Breakdown.PolicyNumber);
            Assert.Equal(new DateOnly(2028, 3, 1), v.Breakdown.Expiry);
        }
    }

    /// <summary>
    /// Breakdown cover deliberately drives no countdown: <c>RenewalCalculator</c> reads MOT, insurance and road
    /// tax only. Pinned so that adding a fourth renewal is a decision someone makes, not something that arrives
    /// by accident with a patch.
    /// </summary>
    [Fact]
    public async Task Breakdown_cover_drives_no_renewal_countdown()
    {
        var vehicleId = await SeedVehicleAsync("VUP9 III");

        await using var context = NewContext();
        var result = await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(
            Breakdown: new BreakdownPatch(Provider: "RAC", Expiry: new DateOnly(2026, 8, 1))));

        Assert.Equal(WriteStatus.Updated, result.Status);
        var renewals = result.Value!.Renewals;
        Assert.Null(renewals.Insurance.ExpiryDate);
        Assert.Null(renewals.RoadTax.ExpiryDate);
    }

    [Fact]
    public async Task Fuel_tank_capacity_is_set_when_given_and_left_untouched_when_the_block_is_absent()
    {
        var vehicleId = await SeedVehicleAsync("VUP4 DDD");

        await using (var context = NewContext())
        {
            await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(Fluids: new FluidsPatch(59m)));
        }

        // A later patch with no Fluids block must not clear the capacity — only edit the colour.
        await using (var context = NewContext())
        {
            await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(Colour: "Epsom Green"));
        }

        await using (var reader = NewContext())
        {
            var vehicle = await reader.Vehicles.SingleAsync(v => v.Id == vehicleId);
            Assert.Equal(59m, vehicle.Fluids.FuelTankCapacityLitres);
            Assert.Equal("Epsom Green", vehicle.Colour);
        }
    }

    // ---- Clear list -------------------------------------------------------------------------------------------
    // A null in a patch means "unchanged" (the MCP settings tools rely on it), so clearing a stored value goes
    // through VehiclePatch.Clear. The rule: clears apply after the merge, a field both set and cleared is a 400
    // on that field, and an owned block that does not exist stays that way.

    /// <summary>Every clearable member set to a non-null value, so a clear has something to remove.</summary>
    private static VehiclePatch EverythingSet(IReadOnlyList<VehicleField>? clear = null) => new(
        Colour: "Epsom Green",
        Vin: "SALLNABG83A123456",
        BodyStyle: "5-door SUV",
        Seller: "A. Seller",
        DefaultGarage: "K & P Motors",
        Notes: "Head gasket done 2025",
        MotExpirySeed: new DateOnly(2027, 7, 8),
        VedExpiry: new DateOnly(2027, 1, 31),
        VedAnnualCost: 430m,
        UlezCompliant: true,
        Insurance: new InsurancePatch(
            Insurer: "Admiral", PolicyNumber: "P77904683",
            PeriodStart: new DateOnly(2026, 2, 1), PeriodEnd: new DateOnly(2027, 1, 31),
            CoverType: "Comprehensive", Premium: 517.14m,
            ExcessCompulsory: 250m, ExcessVoluntary: 100m, NcbYears: 9),
        Fluids: new FluidsPatch(
            FuelTankCapacityLitres: 59m, OilSpec: "5W-30 A3/B4", OilCapacityLitres: 4.8m,
            CoolantSpec: "OAT (red)", CoolantCapacityLitres: 7m, BrakeFluidSpec: "DOT 4",
            TransmissionOilSpec: "75W-80", SparkPlugPart: "NGK BKR6E", OilFilterPart: "W712/75",
            AirFilterPart: "PHE100500", FuelFilterPart: "WFL000010", CabinFilterPart: "JKR100120"),
        Tyres: new TyresPatch(
            TyreSize: "215/65 R16", PressureFrontPsi: 30m, PressureRearPsi: 33m,
            PressureFrontLadenPsi: 32m, PressureRearLadenPsi: 38m, MinTreadMm: 1.6m),
        PurchasePrice: 1750m,
        Breakdown: new BreakdownPatch(Provider: "Green Flag", PolicyNumber: "GF-88213", Expiry: new DateOnly(2027, 3, 1)),
        Clear: clear);

    /// <summary>
    /// Every clearable column, read straight off the row and keyed by the member that should clear it. Written
    /// out by hand rather than shared with the service, so the test is a second opinion on which column each
    /// member owns.
    /// </summary>
    private static Dictionary<VehicleField, object?> Columns(Vehicle v) => new()
    {
        [VehicleField.Colour] = v.Colour,
        [VehicleField.Vin] = v.Vin,
        [VehicleField.BodyStyle] = v.BodyStyle,
        [VehicleField.Seller] = v.Seller,
        [VehicleField.DefaultGarage] = v.DefaultGarage,
        [VehicleField.Notes] = v.Notes,
        [VehicleField.MotExpirySeed] = v.MotExpirySeed,
        [VehicleField.VedExpiry] = v.VedExpiry,
        [VehicleField.VedAnnualCost] = v.VedAnnualCost,
        [VehicleField.UlezCompliant] = v.UlezCompliant,
        [VehicleField.PurchasePrice] = v.PurchasePrice,
        [VehicleField.InsuranceInsurer] = v.Insurance?.Insurer,
        [VehicleField.InsurancePolicyNumber] = v.Insurance?.PolicyNumber,
        [VehicleField.InsurancePeriodStart] = v.Insurance?.PeriodStart,
        [VehicleField.InsurancePeriodEnd] = v.Insurance?.PeriodEnd,
        [VehicleField.InsuranceCoverType] = v.Insurance?.CoverType,
        [VehicleField.InsurancePremium] = v.Insurance?.Premium,
        [VehicleField.InsuranceExcessCompulsory] = v.Insurance?.ExcessCompulsory,
        [VehicleField.InsuranceExcessVoluntary] = v.Insurance?.ExcessVoluntary,
        [VehicleField.InsuranceNcbYears] = v.Insurance?.NcbYears,
        [VehicleField.BreakdownProvider] = v.Breakdown?.Provider,
        [VehicleField.BreakdownPolicyNumber] = v.Breakdown?.PolicyNumber,
        [VehicleField.BreakdownExpiry] = v.Breakdown?.Expiry,
        [VehicleField.FluidsFuelTankCapacityLitres] = v.Fluids.FuelTankCapacityLitres,
        [VehicleField.FluidsOilSpec] = v.Fluids.OilSpec,
        [VehicleField.FluidsOilCapacityLitres] = v.Fluids.OilCapacityLitres,
        [VehicleField.FluidsCoolantSpec] = v.Fluids.CoolantSpec,
        [VehicleField.FluidsCoolantCapacityLitres] = v.Fluids.CoolantCapacityLitres,
        [VehicleField.FluidsBrakeFluidSpec] = v.Fluids.BrakeFluidSpec,
        [VehicleField.FluidsTransmissionOilSpec] = v.Fluids.TransmissionOilSpec,
        [VehicleField.FluidsSparkPlugPart] = v.Fluids.SparkPlugPart,
        [VehicleField.FluidsOilFilterPart] = v.Fluids.OilFilterPart,
        [VehicleField.FluidsAirFilterPart] = v.Fluids.AirFilterPart,
        [VehicleField.FluidsFuelFilterPart] = v.Fluids.FuelFilterPart,
        [VehicleField.FluidsCabinFilterPart] = v.Fluids.CabinFilterPart,
        [VehicleField.TyresTyreSize] = v.Tyres.TyreSize,
        [VehicleField.TyresPressureFrontPsi] = v.Tyres.PressureFrontPsi,
        [VehicleField.TyresPressureRearPsi] = v.Tyres.PressureRearPsi,
        [VehicleField.TyresPressureFrontLadenPsi] = v.Tyres.PressureFrontLadenPsi,
        [VehicleField.TyresPressureRearLadenPsi] = v.Tyres.PressureRearLadenPsi,
        [VehicleField.TyresMinTreadMm] = v.Tyres.MinTreadMm,
    };

    /// <summary>
    /// The web form's key for each member, which is what a conflict error is keyed by so <c>reportApiError</c>
    /// lands it on the right input. Must match the <c>key</c> of the <c>FieldSpec</c> that <c>clears</c> it.
    /// </summary>
    private static readonly Dictionary<VehicleField, string> FormKeys = new()
    {
        [VehicleField.Colour] = "colour",
        [VehicleField.Vin] = "vin",
        [VehicleField.BodyStyle] = "bodyStyle",
        [VehicleField.Seller] = "seller",
        [VehicleField.DefaultGarage] = "defaultGarage",
        [VehicleField.Notes] = "notes",
        [VehicleField.MotExpirySeed] = "motExpirySeed",
        [VehicleField.VedExpiry] = "vedExpiry",
        [VehicleField.VedAnnualCost] = "vedAnnualCost",
        [VehicleField.UlezCompliant] = "ulezCompliant",
        [VehicleField.PurchasePrice] = "purchasePrice",
        [VehicleField.InsuranceInsurer] = "insurer",
        [VehicleField.InsurancePolicyNumber] = "policyNumber",
        [VehicleField.InsurancePeriodStart] = "periodStart",
        [VehicleField.InsurancePeriodEnd] = "periodEnd",
        [VehicleField.InsuranceCoverType] = "coverType",
        [VehicleField.InsurancePremium] = "premium",
        [VehicleField.InsuranceExcessCompulsory] = "excessCompulsory",
        [VehicleField.InsuranceExcessVoluntary] = "excessVoluntary",
        [VehicleField.InsuranceNcbYears] = "ncbYears",
        [VehicleField.BreakdownProvider] = "provider",
        [VehicleField.BreakdownPolicyNumber] = "policyNumber",
        [VehicleField.BreakdownExpiry] = "expiry",
        [VehicleField.FluidsFuelTankCapacityLitres] = "fuelTankCapacityLitres",
        [VehicleField.FluidsOilSpec] = "oilSpec",
        [VehicleField.FluidsOilCapacityLitres] = "oilCapacityLitres",
        [VehicleField.FluidsCoolantSpec] = "coolantSpec",
        [VehicleField.FluidsCoolantCapacityLitres] = "coolantCapacityLitres",
        [VehicleField.FluidsBrakeFluidSpec] = "brakeFluidSpec",
        [VehicleField.FluidsTransmissionOilSpec] = "transmissionOilSpec",
        [VehicleField.FluidsSparkPlugPart] = "sparkPlugPart",
        [VehicleField.FluidsOilFilterPart] = "oilFilterPart",
        [VehicleField.FluidsAirFilterPart] = "airFilterPart",
        [VehicleField.FluidsFuelFilterPart] = "fuelFilterPart",
        [VehicleField.FluidsCabinFilterPart] = "cabinFilterPart",
        [VehicleField.TyresTyreSize] = "tyreSize",
        [VehicleField.TyresPressureFrontPsi] = "pressureFrontPsi",
        [VehicleField.TyresPressureRearPsi] = "pressureRearPsi",
        [VehicleField.TyresPressureFrontLadenPsi] = "pressureFrontLadenPsi",
        [VehicleField.TyresPressureRearLadenPsi] = "pressureRearLadenPsi",
        [VehicleField.TyresMinTreadMm] = "minTreadMm",
    };

    private async Task<Vehicle> ReloadAsync(int vehicleId)
    {
        await using var reader = NewContext();
        return await reader.Vehicles.AsNoTracking().SingleAsync(v => v.Id == vehicleId);
    }

    private async Task<WriteResult<VehicleSummary>> PatchAsync(int vehicleId, VehiclePatch patch)
    {
        await using var context = NewContext();
        return await NewService(context).ApplyAsync(vehicleId, patch);
    }

    [Fact]
    public async Task Clearing_the_oil_spec_nulls_it_and_leaves_the_other_fluids()
    {
        var vehicleId = await SeedVehicleAsync("VUC1 AAA");
        Assert.Equal(WriteStatus.Updated, (await PatchAsync(vehicleId, EverythingSet())).Status);
        var before = Columns(await ReloadAsync(vehicleId));

        var result = await PatchAsync(vehicleId, new VehiclePatch(Clear: [VehicleField.FluidsOilSpec]));

        Assert.Equal(WriteStatus.Updated, result.Status);
        var after = Columns(await ReloadAsync(vehicleId));
        Assert.Null(after[VehicleField.FluidsOilSpec]);
        foreach (var member in after.Keys.Where(k => k != VehicleField.FluidsOilSpec))
            Assert.Equal(before[member], after[member]);
    }

    /// <summary>
    /// The enumerated proof: a new <see cref="VehicleField"/> member without a handler, or with one pointed at the
    /// wrong column, fails here. The column map is checked for completeness first, so it cannot fall behind the
    /// enum and pass by checking less.
    /// </summary>
    [Fact]
    public async Task Every_vehicle_field_clears_exactly_its_own_column()
    {
        var vehicleId = await SeedVehicleAsync("VUC2 BBB");
        Assert.Equal(WriteStatus.Updated, (await PatchAsync(vehicleId, EverythingSet())).Status);
        var populated = Columns(await ReloadAsync(vehicleId));

        Assert.Equal(Enum.GetValues<VehicleField>().Order(), populated.Keys.Order());
        Assert.All(populated, kv => Assert.NotNull(kv.Value));

        foreach (var member in Enum.GetValues<VehicleField>())
        {
            var result = await PatchAsync(vehicleId, new VehiclePatch(Clear: [member]));
            Assert.True(result.Status == WriteStatus.Updated, $"{member}: {result.Status}");

            var after = Columns(await ReloadAsync(vehicleId));
            foreach (var column in after.Keys)
            {
                if (column == member) Assert.True(after[column] is null, $"{member} did not clear its own column");
                else Assert.True(Equals(populated[column], after[column]), $"clearing {member} changed {column}");
            }

            // Put it back for the next member.
            Assert.Equal(WriteStatus.Updated, (await PatchAsync(vehicleId, EverythingSet())).Status);
        }
    }

    [Fact]
    public async Task Setting_and_clearing_the_oil_spec_is_a_validation_error_and_writes_nothing()
    {
        var vehicleId = await SeedVehicleAsync("VUC3 CCC");
        Assert.Equal(WriteStatus.Updated, (await PatchAsync(vehicleId, EverythingSet())).Status);
        var before = Columns(await ReloadAsync(vehicleId));

        var result = await PatchAsync(vehicleId, new VehiclePatch(
            Colour: "Java Black",
            Fluids: new FluidsPatch(OilSpec: "0W-20"),
            Clear: [VehicleField.FluidsOilSpec, VehicleField.Notes]));

        Assert.Equal(WriteStatus.Validation, result.Status);
        var error = Assert.Single(result.Errors!);
        Assert.Equal("oilSpec", error.Key);
        Assert.Equal("Engine oil cannot be both set and cleared.", Assert.Single(error.Value));

        // Nothing written: not the colour that was valid, not the notes clear that was valid.
        Assert.Equal(before, Columns(await ReloadAsync(vehicleId)));
    }

    [Fact]
    public async Task A_set_and_cleared_field_is_keyed_by_its_form_key_for_every_member()
    {
        var vehicleId = await SeedVehicleAsync("VUC4 DDD");

        Assert.Equal(Enum.GetValues<VehicleField>().Order(), FormKeys.Keys.Order());
        foreach (var member in Enum.GetValues<VehicleField>())
        {
            var result = await PatchAsync(vehicleId, EverythingSet(clear: [member]));

            Assert.True(result.Status == WriteStatus.Validation, $"{member}: {result.Status}");
            var error = Assert.Single(result.Errors!);
            Assert.Equal(FormKeys[member], error.Key);
            Assert.EndsWith(" cannot be both set and cleared.", Assert.Single(error.Value));
        }
    }

    /// <summary>Deliverable 2: the mirror goes with the price, and the dashboard's "car itself" clause with it.</summary>
    [Fact]
    public async Task Clearing_the_purchase_price_removes_the_purchase_expense_in_the_same_save()
    {
        var vehicleId = await SeedVehicleAsync("VUC5 EEE");
        Assert.Equal(WriteStatus.Updated, (await PatchAsync(vehicleId, new VehiclePatch(PurchasePrice: 1750m))).Status);
        await using (var reader = NewContext())
            Assert.True(await reader.ExpenseEntries.AnyAsync(e => e.VehicleId == vehicleId && e.IsVehiclePurchase));

        var result = await PatchAsync(vehicleId, new VehiclePatch(Clear: [VehicleField.PurchasePrice]));

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.Equal(result.Value!.Spend.TotalSincePurchaseExcludingPurchase, result.Value.Spend.TotalSincePurchase);
        await using (var reader = NewContext())
            Assert.False(await reader.ExpenseEntries.AnyAsync(e => e.VehicleId == vehicleId && e.IsVehiclePurchase));
        Assert.Null((await ReloadAsync(vehicleId)).PurchasePrice);
    }

    [Fact]
    public async Task Clearing_an_already_null_field_succeeds_and_changes_nothing()
    {
        var vehicleId = await SeedVehicleAsync("VUC6 FFF");
        Assert.Equal(WriteStatus.Updated, (await PatchAsync(vehicleId, new VehiclePatch(Colour: "Epsom Green"))).Status);
        var before = Columns(await ReloadAsync(vehicleId));

        var result = await PatchAsync(vehicleId, new VehiclePatch(Clear: [VehicleField.Vin, VehicleField.TyresMinTreadMm]));

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.Equal(before, Columns(await ReloadAsync(vehicleId)));
    }

    [Fact]
    public async Task A_duplicate_entry_in_the_clear_list_is_not_an_error()
    {
        var vehicleId = await SeedVehicleAsync("VUC7 GGG");
        Assert.Equal(WriteStatus.Updated, (await PatchAsync(vehicleId, new VehiclePatch(Notes: "Wrong car"))).Status);

        var result = await PatchAsync(vehicleId, new VehiclePatch(Clear: [VehicleField.Notes, VehicleField.Notes]));

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.Null((await ReloadAsync(vehicleId)).Notes);
    }

    /// <summary>
    /// The merge creates an owned block with <c>??=</c> when a patch carries one. A clear has nothing to put in
    /// it, so on a vehicle with no insurance or breakdown cover it must succeed and leave every column of both
    /// blocks null. (The entity initialises both blocks, so "no block" and "an empty block" are the same row.)
    /// </summary>
    [Fact]
    public async Task Clearing_insurance_or_breakdown_on_a_vehicle_without_them_leaves_both_blocks_empty()
    {
        var vehicleId = await SeedVehicleAsync("VUC8 HHH");
        var before = Columns(await ReloadAsync(vehicleId));

        var result = await PatchAsync(vehicleId, new VehiclePatch(
            Clear: [VehicleField.InsuranceInsurer, VehicleField.InsuranceNcbYears, VehicleField.BreakdownProvider]));

        Assert.Equal(WriteStatus.Updated, result.Status);
        var after = Columns(await ReloadAsync(vehicleId));
        Assert.Equal(before, after);
        Assert.All(
            after.Where(kv => kv.Key.ToString().StartsWith("Insurance") || kv.Key.ToString().StartsWith("Breakdown")),
            kv => Assert.Null(kv.Value));
        Assert.Null(result.Value!.Renewals.Insurance.ExpiryDate);
    }

    [Fact]
    public async Task Clearing_the_default_garage_leaves_the_garage_reference_row()
    {
        var vehicleId = await SeedVehicleAsync("VUC9 III");
        Assert.Equal(WriteStatus.Updated,
            (await PatchAsync(vehicleId, new VehiclePatch(DefaultGarage: "Clearwater Motors"))).Status);

        var result = await PatchAsync(vehicleId, new VehiclePatch(Clear: [VehicleField.DefaultGarage]));

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.Null((await ReloadAsync(vehicleId)).DefaultGarage);
        await using var reader = NewContext();
        Assert.True(await reader.Garages.AnyAsync(g => g.Name == "Clearwater Motors"));
    }

    /// <summary>
    /// The MCP settings tools build patches with <c>Clear</c> left at its default. A null list must mean exactly
    /// what it meant before the list existed: set what is given, leave the rest.
    /// </summary>
    [Fact]
    public async Task A_patch_with_no_clear_list_merges_as_before()
    {
        var vehicleId = await SeedVehicleAsync("VUC10 JJ");
        Assert.Equal(WriteStatus.Updated, (await PatchAsync(vehicleId, EverythingSet())).Status);
        var before = Columns(await ReloadAsync(vehicleId));

        var result = await PatchAsync(vehicleId, new VehiclePatch(Fluids: new FluidsPatch(OilSpec: "10W-40"), Clear: null));

        Assert.Equal(WriteStatus.Updated, result.Status);
        var after = Columns(await ReloadAsync(vehicleId));
        Assert.Equal("10W-40", after[VehicleField.FluidsOilSpec]);
        foreach (var member in after.Keys.Where(k => k != VehicleField.FluidsOilSpec))
            Assert.Equal(before[member], after[member]);
    }
}
