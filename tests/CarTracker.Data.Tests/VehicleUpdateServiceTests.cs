using CarTracker.Domain;
using CarTracker.Domain.Vehicles;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
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
}
