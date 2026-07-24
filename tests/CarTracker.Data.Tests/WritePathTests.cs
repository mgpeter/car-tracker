using CarTracker.Domain;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The write paths, against a real database — the fuel mirror and the detectors that run after every save.
/// </summary>
/// <remarks>
/// Here rather than in Domain.Tests because these are claims about transactions, foreign keys and what
/// actually landed in which table. A stubbed context would assert that the code I wrote calls the methods I
/// wrote.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class WritePathTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private int _ownerId;

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>()
                .UseNpgsql(_connectionString)
                .Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero)));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_writes");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> NewVehicleAsync(CarTrackerDbContext context, string registration)
    {
        var vehicle = new Vehicle
        {
            Registration = registration,
            Make = "Land Rover",
            Model = "Freelander 1",
            Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14),
            PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol,
            Source = EntrySource.Web,
        };

        await new VehicleFactory(context).CreateAsync(vehicle, _ownerId, EntrySource.Web);
        return vehicle.Id;
    }

    private static FuelEntry Fill(int vehicleId, DateOnly date, int mileage, decimal litres, decimal price) => new()
    {
        VehicleId = vehicleId,
        EntryDate = date,
        Mileage = mileage,
        Litres = litres,
        PricePerLitre = price,
        TotalCost = decimal.Round(litres * price, 2),
        Station = "Tesco Kingston",
        FillLevel = FillLevel.Full,
    };

    // ---- The fuel mirror (§3.2) ---------------------------------------------------------------------------

    [Fact]
    public async Task A_fill_writes_its_odometer_reading_and_its_expense()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "WR11 TE1");

        var entry = Fill(vehicleId, new DateOnly(2026, 4, 30), 77_405, 40m, 1.50m);
        await new FuelEntryFactory(context).CreateAsync(entry, EntrySource.Web);

        // A fill is never one row. Miss any of the three and a figure downstream goes quietly wrong.
        Assert.Equal(1, await context.FuelEntries.CountAsync(f => f.VehicleId == vehicleId));

        var reading = await context.MileageReadings
            .SingleAsync(m => m.VehicleId == vehicleId && m.Origin == MileageOrigin.Fuel);
        Assert.Equal(77_405, reading.Mileage);

        var expense = await context.ExpenseEntries.SingleAsync(e => e.VehicleId == vehicleId);
        Assert.Equal("Fuel", expense.Category);
        Assert.Equal(60m, expense.Amount);
        Assert.Equal(entry.Id, expense.FuelEntryId);
    }

    [Fact]
    public async Task The_mirrored_expenses_total_equals_the_fuel_log_to_the_penny()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "WR11 TE2");
        var factory = new FuelEntryFactory(context);

        await factory.CreateAsync(Fill(vehicleId, new DateOnly(2026, 4, 30), 77_405, 40.00m, 1.549m), EntrySource.Web);
        await factory.CreateAsync(Fill(vehicleId, new DateOnly(2026, 5, 6), 77_679, 43.25m, 1.571m), EntrySource.Web);
        await factory.CreateAsync(Fill(vehicleId, new DateOnly(2026, 5, 9), 77_958, 41.31m, 1.585m), EntrySource.Web);

        var fuelLog = await context.FuelEntries.Where(f => f.VehicleId == vehicleId).SumAsync(f => f.TotalCost);
        var mirrored = await context.ExpenseEntries
            .Where(e => e.VehicleId == vehicleId && e.Category == "Fuel")
            .SumAsync(e => e.Amount);

        // The workbook's fourth defect, made impossible: its Expenses sheet carried one lumped "fuel to date"
        // row of £725.70 against a Fuel Log of £888.86 — a £163.16 gap, because the two were maintained by
        // hand. These are the same number by construction now, not by discipline.
        Assert.Equal(fuelLog, mirrored);
    }

    [Fact]
    public async Task The_receipt_total_wins_over_litres_times_price()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "WR11 TE3");

        var entry = Fill(vehicleId, new DateOnly(2026, 4, 30), 77_405, 47.03m, 1.799m);
        entry.TotalCost = 84.61m; // the receipt; 47.03 x 1.799 = 84.61 to the penny either way, but the
                                  // receipt is the authority when they disagree.
        await new FuelEntryFactory(context).CreateAsync(entry, EntrySource.Web);

        var expense = await context.ExpenseEntries.SingleAsync(e => e.VehicleId == vehicleId);
        Assert.Equal(84.61m, expense.Amount);
    }

    // ---- Editing and removing a fill or service, shadows and all (log-entry-edit-remove) ------------------

    [Fact]
    public async Task Editing_a_fill_drags_its_reading_and_expense_along()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDT 111");
        var factory = new FuelEntryFactory(context);

        var entry = Fill(vehicleId, new DateOnly(2026, 5, 6), 77_679, 43.25m, 1.571m);
        await factory.CreateAsync(entry, EntrySource.Web);

        // Correct a mistyped odometer and litres — the receipt total follows the new litres x price.
        var originalDate = entry.EntryDate;
        var originalMileage = entry.Mileage;
        entry.Mileage = 77_698;
        entry.Litres = 42.00m;
        entry.TotalCost = decimal.Round(42.00m * 1.571m, 2);
        await factory.UpdateAsync(entry, originalDate, originalMileage);

        // Both shadows moved with it — still one of each, not a duplicate left at the old figure.
        var reading = await context.MileageReadings
            .SingleAsync(m => m.VehicleId == vehicleId && m.Origin == MileageOrigin.Fuel);
        Assert.Equal(77_698, reading.Mileage);

        var expense = await context.ExpenseEntries.SingleAsync(e => e.FuelEntryId == entry.Id);
        Assert.Equal(entry.TotalCost, expense.Amount);
        Assert.Equal(77_698, expense.Mileage);
    }

    [Fact]
    public async Task Deleting_a_fill_takes_its_reading_and_expense_with_it()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDT 222");
        var factory = new FuelEntryFactory(context);

        var entry = Fill(vehicleId, new DateOnly(2026, 5, 6), 77_679, 43.25m, 1.571m);
        await factory.CreateAsync(entry, EntrySource.Web);

        await factory.DeleteAsync(entry);

        // A fill was never one row, and neither is its removal — the mistyped fill is gone completely.
        Assert.Equal(0, await context.FuelEntries.CountAsync(f => f.VehicleId == vehicleId));
        Assert.Empty(await context.MileageReadings
            .Where(m => m.VehicleId == vehicleId && m.Origin == MileageOrigin.Fuel).ToListAsync());
        Assert.Empty(await context.ExpenseEntries.Where(e => e.VehicleId == vehicleId).ToListAsync());
    }

    [Fact]
    public async Task Editing_a_service_updates_its_reading_and_mirrored_expense()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "SVC 111");
        var factory = NewServiceFactory(context);

        var record = new ServiceRecord
        {
            VehicleId = vehicleId, ServiceDate = new DateOnly(2026, 6, 1), Type = "Service",
            Mileage = 80_000, Cost = 603.99m, Source = EntrySource.Web,
        };
        await factory.CreateAsync(record, EntrySource.Web);

        var originalDate = record.ServiceDate;
        var originalMileage = record.Mileage;
        record.Cost = 650.00m;
        record.Mileage = 80_050;
        await factory.UpdateAsync(record, originalDate, originalMileage);

        var expense = await context.ExpenseEntries.SingleAsync(e => e.ServiceRecordId == record.Id);
        Assert.Equal(650.00m, expense.Amount);
        Assert.Equal(80_050, expense.Mileage);

        var reading = await context.MileageReadings
            .SingleAsync(m => m.VehicleId == vehicleId && m.Origin == MileageOrigin.Service);
        Assert.Equal(80_050, reading.Mileage);
    }

    [Fact]
    public async Task A_service_cost_added_on_edit_creates_the_mirror_and_removed_deletes_it()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "SVC 222");
        var factory = NewServiceFactory(context);

        // No cost: a DIY job with no money mirrors nothing.
        var record = new ServiceRecord
        {
            VehicleId = vehicleId, ServiceDate = new DateOnly(2026, 6, 1), Type = "Service",
            Mileage = 80_000, Cost = null, Source = EntrySource.Web,
        };
        await factory.CreateAsync(record, EntrySource.Web);
        Assert.Empty(await context.ExpenseEntries.Where(e => e.ServiceRecordId == record.Id).ToListAsync());

        // A cost added on edit: the mirror must now exist.
        record.Cost = 120m;
        await factory.UpdateAsync(record, record.ServiceDate, record.Mileage);
        Assert.Equal(120m, (await context.ExpenseEntries.SingleAsync(e => e.ServiceRecordId == record.Id)).Amount);

        // A cost removed on edit: mirroring £0 would log money that was not spent, so the mirror goes.
        record.Cost = null;
        await factory.UpdateAsync(record, record.ServiceDate, record.Mileage);
        Assert.Empty(await context.ExpenseEntries.Where(e => e.ServiceRecordId == record.Id).ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_service_record_takes_its_reading_and_expense_with_it()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "SVC 333");
        var factory = NewServiceFactory(context);

        var record = new ServiceRecord
        {
            VehicleId = vehicleId, ServiceDate = new DateOnly(2026, 6, 1), Type = "Service",
            Mileage = 80_000, Cost = 603.99m, Source = EntrySource.Web,
        };
        await factory.CreateAsync(record, EntrySource.Web);

        await factory.DeleteAsync(record);

        Assert.Equal(0, await context.ServiceRecords.CountAsync(r => r.VehicleId == vehicleId));
        Assert.Empty(await context.MileageReadings
            .Where(m => m.VehicleId == vehicleId && m.Origin == MileageOrigin.Service).ToListAsync());
        Assert.Empty(await context.ExpenseEntries.Where(e => e.VehicleId == vehicleId).ToListAsync());
    }

    [Fact]
    public async Task Correcting_the_litres_that_tripped_an_implausible_flag_clears_it()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDT 333");
        var factory = new FuelEntryFactory(context);

        await factory.CreateAsync(Fill(vehicleId, new DateOnly(2026, 6, 1), 80_000, 40m, 1.50m), EntrySource.Web);
        // 300 miles on a 5 L splash: 272 mpg — an implausible fill, almost certainly a mistyped litres.
        var splash = Fill(vehicleId, new DateOnly(2026, 6, 10), 80_300, 5m, 1.50m);
        await factory.CreateAsync(splash, EntrySource.Web);

        var scanner = NewScanner(context);
        Assert.Single(await scanner.ScanAsync(vehicleId, EntrySource.Web));

        // The owner corrects it: it was 45 litres, not 5.
        var originalDate = splash.EntryDate;
        var originalMileage = splash.Mileage;
        splash.Litres = 45m;
        splash.TotalCost = decimal.Round(45m * 1.50m, 2);
        await factory.UpdateAsync(splash, originalDate, originalMileage);

        // The next scan finds the interval plausible, so the flag auto-reconciles to Corrected — the whole
        // point of editing rather than deleting-and-re-adding.
        Assert.Empty(await scanner.ScanAsync(vehicleId, EntrySource.Web));
        var flag = await context.DataAnomalies.SingleAsync(a => a.VehicleId == vehicleId);
        Assert.Equal(AnomalyStatus.Corrected, flag.Status);
        Assert.Contains("Auto-resolved", flag.ResolutionNote);
    }

    // ---- The detectors, running for real ------------------------------------------------------------------

    [Fact]
    public async Task A_clean_history_raises_nothing()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "WR11 TE4");
        await new FuelEntryFactory(context).CreateAsync(
            Fill(vehicleId, new DateOnly(2026, 4, 30), 77_405, 40m, 1.50m), EntrySource.Web);

        var flags = await NewScanner(context).ScanAsync(vehicleId, EntrySource.Web);

        // The normal case, and worth pinning: a detector that fires on ordinary data is worse than none.
        Assert.Empty(flags);
    }

    [Fact]
    public async Task A_reading_above_the_odometer_is_recorded_flagged_and_then_ignored()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "WR11 TE5");
        var fuel = new FuelEntryFactory(context);

        await fuel.CreateAsync(Fill(vehicleId, new DateOnly(2026, 6, 24), 80_215, 41.74m, 1.564m), EntrySource.Web);
        await fuel.CreateAsync(Fill(vehicleId, new DateOnly(2026, 7, 10), 80_712, 47.03m, 1.799m), EntrySource.Web);

        // The workbook's third defect, reproduced exactly: a service record dated 27 Jun logging 83,000 mi —
        // almost certainly 80,300 mistyped — against a real odometer of 80,712.
        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = new DateOnly(2026, 6, 27),
            Mileage = 83_000,
            Origin = MileageOrigin.Service,
            Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var flags = await NewScanner(context).ScanAsync(vehicleId, EntrySource.Web);

        // Exactly one, and it names the culprit rather than its innocent successors.
        var flag = Assert.Single(flags);
        Assert.Equal(AnomalyKind.MileageNonMonotonic, flag.Kind);
        Assert.Equal(AnomalySeverity.Error, flag.Severity);

        // Recorded, not refused (§5.3). Refusing would push the owner into editing the number until the app
        // accepts it — the same outcome as the spreadsheet, with more steps.
        Assert.Equal(83_000, await context.MileageReadings
            .Where(m => m.VehicleId == vehicleId).MaxAsync(m => m.Mileage));

        // And the odometer does not move. This is the entire product thesis in one assertion.
        var summary = await NewMetrics(context).GetVehicleSummaryAsync(vehicleId);
        Assert.NotNull(summary);
        Assert.Equal(80_712, summary.Mileage.CurrentMileage);
        Assert.True(summary.Mileage.HasNonMonotonicHistory);
    }

    [Fact]
    public async Task The_same_flag_is_not_raised_twice()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "WR11 TE6");
        await new FuelEntryFactory(context).CreateAsync(
            Fill(vehicleId, new DateOnly(2026, 7, 10), 80_712, 47.03m, 1.799m), EntrySource.Web);

        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = new DateOnly(2026, 6, 27),
            Mileage = 83_000,
            Origin = MileageOrigin.Service,
            Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var scanner = NewScanner(context);
        Assert.Single(await scanner.ScanAsync(vehicleId, EntrySource.Web));

        // Every subsequent write re-scans the whole history. Without de-duplication the owner would collect a
        // new copy of the same flag on every fill, and the integrity queue would become noise to scroll past
        // — which is how a warning stops being a warning.
        Assert.Empty(await scanner.ScanAsync(vehicleId, EntrySource.Web));
        Assert.Equal(1, await context.DataAnomalies.CountAsync(a => a.VehicleId == vehicleId));
    }

    [Fact]
    public async Task A_resolved_flag_does_not_come_back()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "WR11 TE7");
        await new FuelEntryFactory(context).CreateAsync(
            Fill(vehicleId, new DateOnly(2026, 7, 10), 80_712, 47.03m, 1.799m), EntrySource.Web);

        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = new DateOnly(2026, 6, 27),
            Mileage = 83_000,
            Origin = MileageOrigin.Service,
            Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var scanner = NewScanner(context);
        var raised = Assert.Single(await scanner.ScanAsync(vehicleId, EntrySource.Web));

        // The owner decides: "yes, that really is what the garage wrote."
        //
        // ResolvedAt is not optional here — ck_anomalies_resolved_iff_terminal enforces
        // `(status = 'Open') = (resolved_at IS NULL)`, so a flag cannot be resolved without recording when.
        // The first draft of this test set only the status and the database refused it, which is the schema
        // doing precisely the job the in-memory provider would have skipped.
        var stored = await context.DataAnomalies.SingleAsync(a => a.Id == raised.Id);
        stored.Status = AnomalyStatus.Accepted;
        stored.ResolvedAt = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
        stored.ResolutionNote = "Confirmed against the invoice.";
        await context.SaveChangesAsync();

        // Decided means decided. Re-raising it would overrule a human with a rule, and Accept would mean
        // nothing.
        Assert.Empty(await scanner.ScanAsync(vehicleId, EntrySource.Web));
    }

    [Fact]
    public async Task A_corrected_flag_can_be_raised_again_because_the_data_changed()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "WR11 TE9");
        await new FuelEntryFactory(context).CreateAsync(
            Fill(vehicleId, new DateOnly(2026, 7, 10), 80_712, 47.03m, 1.799m), EntrySource.Web);

        var bad = new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = new DateOnly(2026, 6, 27),
            Mileage = 83_000,
            Origin = MileageOrigin.Service,
            Source = EntrySource.Web,
        };
        context.MileageReadings.Add(bad);
        await context.SaveChangesAsync();

        var scanner = NewScanner(context);
        var raised = Assert.Single(await scanner.ScanAsync(vehicleId, EntrySource.Web));

        // The owner fixes the typo: 83,000 was 80,300.
        var stored = await context.DataAnomalies.SingleAsync(a => a.Id == raised.Id);
        stored.Status = AnomalyStatus.Corrected;
        stored.ResolvedAt = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
        stored.ResolutionNote = "Mistyped; the invoice says 80,300.";
        bad.Mileage = 80_300;
        await context.SaveChangesAsync();

        // Nothing to find — the reading is in sequence now.
        Assert.Empty(await scanner.ScanAsync(vehicleId, EntrySource.Web));

        // But Corrected does NOT suppress forever, and this is the half the original rule got right: the data
        // was CHANGED, so if it goes bad again that is a new fact about a different value, not the question
        // the owner already answered.
        bad.Mileage = 91_000;
        await context.SaveChangesAsync();

        var again = Assert.Single(await scanner.ScanAsync(vehicleId, EntrySource.Web));
        Assert.Equal(AnomalyKind.MileageNonMonotonic, again.Kind);
    }

    [Fact]
    public async Task A_flag_auto_resolves_when_its_cause_is_deleted()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REC 111");
        await new FuelEntryFactory(context).CreateAsync(
            Fill(vehicleId, new DateOnly(2026, 7, 10), 80_712, 47.03m, 1.799m), EntrySource.Web);

        var bad = new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = new DateOnly(2026, 6, 27),
            Mileage = 83_000,
            Origin = MileageOrigin.Service,
            Source = EntrySource.Web,
        };
        context.MileageReadings.Add(bad);
        await context.SaveChangesAsync();

        var scanner = NewScanner(context);
        var raised = Assert.Single(await scanner.ScanAsync(vehicleId, EntrySource.Web));
        Assert.Equal(AnomalyStatus.Open, raised.Status);

        // The owner deletes the stray reading. Found live this session: before auto-reconcile, the Open flag
        // it raised sat there orphaned, pointing at a row that was gone, and had to be resolved by hand.
        context.MileageReadings.Remove(bad);
        await context.SaveChangesAsync();

        // Next scan: the condition is no longer present, so the flag closes itself — nothing raised.
        Assert.Empty(await scanner.ScanAsync(vehicleId, EntrySource.Web));

        var stored = await context.DataAnomalies.SingleAsync(a => a.Id == raised.Id);
        Assert.Equal(AnomalyStatus.Corrected, stored.Status);
        // ResolvedAt moves with the status or ck_anomalies_resolved_iff_terminal rejects the save — the fact
        // this row persisted at all is the constraint being satisfied.
        Assert.NotNull(stored.ResolvedAt);
        Assert.Contains("Auto-resolved", stored.ResolutionNote);

        // Kept, not deleted: the row and its reason survive under ?status=all, per the queue's own promise.
        Assert.Equal(1, await context.DataAnomalies.CountAsync(a => a.VehicleId == vehicleId));

        // Idempotent: a second scan over unchanged data must not re-touch the timestamp or status.
        var resolvedAt = stored.ResolvedAt;
        Assert.Empty(await scanner.ScanAsync(vehicleId, EntrySource.Web));
        var reloaded = await context.DataAnomalies.SingleAsync(a => a.Id == raised.Id);
        Assert.Equal(resolvedAt, reloaded.ResolvedAt);
        Assert.Equal(AnomalyStatus.Corrected, reloaded.Status);
    }

    [Fact]
    public async Task An_owner_decision_survives_its_cause_being_deleted()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REC 222");
        await new FuelEntryFactory(context).CreateAsync(
            Fill(vehicleId, new DateOnly(2026, 7, 10), 80_712, 47.03m, 1.799m), EntrySource.Web);

        var bad = new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = new DateOnly(2026, 6, 27),
            Mileage = 83_000,
            Origin = MileageOrigin.Service,
            Source = EntrySource.Web,
        };
        context.MileageReadings.Add(bad);
        await context.SaveChangesAsync();

        var scanner = NewScanner(context);
        var raised = Assert.Single(await scanner.ScanAsync(vehicleId, EntrySource.Web));

        // The owner Accepts it: "that really is what the garage wrote."
        var stored = await context.DataAnomalies.SingleAsync(a => a.Id == raised.Id);
        stored.Status = AnomalyStatus.Accepted;
        stored.ResolvedAt = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
        stored.ResolutionNote = "Confirmed against the invoice.";
        await context.SaveChangesAsync();

        // Even deleting the cause must not overwrite that decision. Auto-reconcile touches Open flags only.
        context.MileageReadings.Remove(bad);
        await context.SaveChangesAsync();
        Assert.Empty(await scanner.ScanAsync(vehicleId, EntrySource.Web));

        var after = await context.DataAnomalies.SingleAsync(a => a.Id == raised.Id);
        Assert.Equal(AnomalyStatus.Accepted, after.Status);
        Assert.Equal("Confirmed against the invoice.", after.ResolutionNote);
    }

    [Fact]
    public async Task An_auto_corrected_flag_re_raises_when_the_condition_returns()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REC 333");
        await new FuelEntryFactory(context).CreateAsync(
            Fill(vehicleId, new DateOnly(2026, 7, 10), 80_712, 47.03m, 1.799m), EntrySource.Web);

        var bad = new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = new DateOnly(2026, 6, 27),
            Mileage = 83_000,
            Origin = MileageOrigin.Service,
            Source = EntrySource.Web,
        };
        context.MileageReadings.Add(bad);
        await context.SaveChangesAsync();

        var scanner = NewScanner(context);
        Assert.Single(await scanner.ScanAsync(vehicleId, EntrySource.Web));

        // The owner edits the typo down so the reading is back in sequence — the same row, condition gone.
        bad.Mileage = 80_300;
        await context.SaveChangesAsync();
        Assert.Empty(await scanner.ScanAsync(vehicleId, EntrySource.Web));

        var autoCorrected = await context.DataAnomalies.SingleAsync(a => a.VehicleId == vehicleId);
        Assert.Equal(AnomalyStatus.Corrected, autoCorrected.Status);
        Assert.Contains("Auto-resolved", autoCorrected.ResolutionNote);

        // Later the same row goes bad again. A Corrected flag — auto or human — does not suppress, because the
        // data was changed: this is a new fact about a different value, and it must be flagged afresh.
        bad.Mileage = 91_000;
        await context.SaveChangesAsync();

        var again = Assert.Single(await scanner.ScanAsync(vehicleId, EntrySource.Web));
        Assert.Equal(AnomalyKind.MileageNonMonotonic, again.Kind);
        Assert.Equal(AnomalyStatus.Open, again.Status);

        // Two rows: the auto-corrected history and the fresh Open flag. The audit trail keeps both.
        Assert.Equal(2, await context.DataAnomalies.CountAsync(a => a.VehicleId == vehicleId));
    }

    [Fact]
    public async Task An_unknown_vehicle_scans_to_nothing_rather_than_throwing()
    {
        await using var context = NewContext();
        Assert.Empty(await NewScanner(context).ScanAsync(999_999, EntrySource.Web));
    }

    [Fact]
    public async Task A_flag_records_who_wrote_it()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "WR11 TE8");
        await new FuelEntryFactory(context).CreateAsync(
            Fill(vehicleId, new DateOnly(2026, 7, 10), 80_712, 47.03m, 1.799m), EntrySource.Web);

        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = new DateOnly(2026, 6, 27),
            Mileage = 83_000,
            Origin = MileageOrigin.Service,
            Source = EntrySource.Mcp,
        });
        await context.SaveChangesAsync();

        // Source matters here: §5.3 requires every MCP write to be audited, and a flag raised by an
        // assistant's write should say so.
        var flag = Assert.Single(await NewScanner(context).ScanAsync(vehicleId, EntrySource.Mcp));
        Assert.Equal(EntrySource.Mcp, flag.Source);
    }

    // ---- Check definitions: the 0-of-18 gap -------------------------------------------------------------

    [Fact]
    public async Task A_new_vehicle_gets_the_generic_starter_set()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "CHK 111");

        var checks = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId)
            .OrderBy(d => d.DisplayOrder)
            .ToListAsync();

        // Before this, CheckDefinition was vehicle-scoped, unseeded, and constructed by nothing — so every
        // car's checks screen showed 0 of 18 forever.
        Assert.Equal(15, checks.Count);
        Assert.Equal("Walk-around: tyres, glass, wipers", checks[0].Name);
        Assert.All(checks, c => Assert.True(c.IsActive));
        Assert.Equal([.. Enumerable.Range(1, 15)], checks.Select(c => c.DisplayOrder));
    }

    [Fact]
    public async Task The_starter_set_is_generic_not_this_car()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "CHK 222");

        var names = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId).Select(d => d.Name).ToListAsync();

        // BT53's three K-series/Freelander checks are NOT in it. Shipping "VCU one-wheel-up rotation test" to
        // a car with no VCU is the same mistake as a seeded vehicle: one car's specifics as everyone's
        // defaults. They are added per-vehicle.
        Assert.DoesNotContain("Oil filler cap underside", names);
        Assert.DoesNotContain("Coolant reservoir colour & level", names);
        Assert.DoesNotContain("VCU one-wheel-up rotation test", names);
    }

    [Fact]
    public async Task A_vehicle_can_be_created_with_no_checks_at_all()
    {
        await using var context = NewContext();
        var vehicle = new Vehicle
        {
            Registration = "CHK 333",
            Make = "Land Rover",
            Model = "Freelander 1",
            Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14),
            PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol,
            Source = EntrySource.Web,
        };

        await new VehicleFactory(context).CreateAsync(vehicle, _ownerId, EntrySource.Web, CheckSource.None);

        Assert.Empty(await context.CheckDefinitions.Where(d => d.VehicleId == vehicle.Id).ToListAsync());
        // The opening reading still lands — that invariant is not negotiable.
        Assert.Single(await context.MileageReadings.Where(m => m.VehicleId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task Checks_can_be_copied_from_another_vehicle()
    {
        await using var context = NewContext();
        var sourceId = await NewVehicleAsync(context, "CHK 444");

        // The owner's own additions — the reason copy-from-existing beats the generic set for a second car.
        context.CheckDefinitions.Add(new CheckDefinition
        {
            VehicleId = sourceId, Name = "Oil filler cap underside", CadenceLabel = "Weekly",
            IntervalDays = 7, DisplayOrder = 99, IsActive = true, Source = EntrySource.Web,
        });
        context.CheckDefinitions.Add(new CheckDefinition
        {
            VehicleId = sourceId, Name = "Retired check", CadenceLabel = "Monthly",
            IntervalDays = 30, DisplayOrder = 98, IsActive = false, Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var copy = new Vehicle
        {
            Registration = "CHK 555",
            Make = "Land Rover",
            Model = "Freelander 1",
            Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14),
            PurchaseMileage = 10_000,
            FuelType = FuelType.Petrol,
            Source = EntrySource.Web,
        };
        await new VehicleFactory(context).CreateAsync(
            copy, _ownerId, EntrySource.Web, CheckSource.CopyFromVehicle, sourceId);

        var names = await context.CheckDefinitions
            .Where(d => d.VehicleId == copy.Id).Select(d => d.Name).ToListAsync();

        Assert.Contains("Oil filler cap underside", names);
        // Active only. An inactive check was switched off deliberately, and carrying that decision onto a
        // different car would be guessing at what someone meant about a car they had not bought yet.
        Assert.DoesNotContain("Retired check", names);
        Assert.Equal(16, names.Count);
    }

    [Fact]
    public async Task Copying_without_a_source_is_refused_before_anything_is_written()
    {
        await using var context = NewContext();
        var vehicle = new Vehicle
        {
            Registration = "CHK 666",
            Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 1,
            FuelType = FuelType.Petrol, Source = EntrySource.Web,
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new VehicleFactory(context).CreateAsync(vehicle, _ownerId, EntrySource.Web, CheckSource.CopyFromVehicle));

        // And nothing was half-created.
        Assert.Empty(await context.Vehicles.Where(v => v.Registration == "CHK 666").ToListAsync());
    }

    private static Vehicle NewStarterVehicle(string registration) => new()
    {
        Registration = registration,
        Make = "Land Rover", Model = "Freelander 1", Year = 2003,
        PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632,
        FuelType = FuelType.Petrol, Source = EntrySource.Web,
    };

    [Fact]
    public async Task A_selected_subset_of_the_starter_set_creates_only_those_checks()
    {
        await using var context = NewContext();
        var vehicle = NewStarterVehicle("SCS 777");

        // Three of the fifteen, given out of template order — the result must come back in template order,
        // renumbered 1..3, and contain nothing else.
        string[] chosen =
        [
            "Tread depth, all 4 tyres",
            "Walk-around: tyres, glass, wipers",
            "Brake fluid level",
        ];

        await new VehicleFactory(context).CreateAsync(
            vehicle, _ownerId, EntrySource.Web, CheckSource.GenericStarterSet, selectedCheckNames: chosen);

        var checks = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicle.Id)
            .OrderBy(d => d.DisplayOrder)
            .ToListAsync();

        Assert.Equal(
            ["Walk-around: tyres, glass, wipers", "Brake fluid level", "Tread depth, all 4 tyres"],
            checks.Select(c => c.Name));
        Assert.Equal([1, 2, 3], checks.Select(c => c.DisplayOrder));
        Assert.All(checks, c => Assert.True(c.IsActive));
    }

    [Fact]
    public async Task Deselecting_every_starter_check_creates_a_vehicle_with_none()
    {
        await using var context = NewContext();
        var vehicle = NewStarterVehicle("CHK 888");

        // An empty selection under the generic source is the deselect-all case: no checks, exactly like None.
        await new VehicleFactory(context).CreateAsync(
            vehicle, _ownerId, EntrySource.Web, CheckSource.GenericStarterSet, selectedCheckNames: []);

        Assert.Empty(await context.CheckDefinitions.Where(d => d.VehicleId == vehicle.Id).ToListAsync());
        // The opening reading still lands — that invariant is not negotiable.
        Assert.Single(await context.MileageReadings.Where(m => m.VehicleId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task A_null_selection_still_creates_the_whole_starter_set()
    {
        await using var context = NewContext();
        var vehicle = NewStarterVehicle("CHK 999");

        // The default path is unchanged: no selection means every generic check, as before.
        await new VehicleFactory(context).CreateAsync(
            vehicle, _ownerId, EntrySource.Web, CheckSource.GenericStarterSet, selectedCheckNames: null);

        Assert.Equal(15, await context.CheckDefinitions.CountAsync(d => d.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task A_selection_is_ignored_when_the_source_is_not_the_generic_set()
    {
        await using var context = NewContext();
        var vehicle = NewStarterVehicle("CHK 010");

        // None draws from no template, so a stray selection changes nothing — still no checks.
        await new VehicleFactory(context).CreateAsync(
            vehicle, _ownerId, EntrySource.Web, CheckSource.None,
            selectedCheckNames: ["Walk-around: tyres, glass, wipers"]);

        Assert.Empty(await context.CheckDefinitions.Where(d => d.VehicleId == vehicle.Id).ToListAsync());
    }

    // ---- Adding a set to an existing vehicle, and copy-with-selection -------------------------------------

    [Fact]
    public async Task Creating_a_vehicle_by_copy_honours_the_selected_names()
    {
        await using var context = NewContext();
        var sourceId = await NewVehicleAsync(context, "ACS 11"); // gets the generic 15

        var target = NewStarterVehicle("ACS 22");
        await new VehicleFactory(context).CreateAsync(
            target, _ownerId, EntrySource.Web, CheckSource.CopyFromVehicle, copyChecksFromVehicleId: sourceId,
            selectedCheckNames: ["Brake fluid level", "Engine oil level"]);

        var names = await context.CheckDefinitions
            .Where(d => d.VehicleId == target.Id).Select(d => d.Name).ToListAsync();

        // Copy is now filterable like the generic set: only the two named checks come across.
        Assert.Equal(2, names.Count);
        Assert.Contains("Brake fluid level", names);
        Assert.Contains("Engine oil level", names);
    }

    [Fact]
    public async Task Adding_a_set_appends_only_the_checks_the_vehicle_lacks()
    {
        await using var context = NewContext();
        var vehicle = NewStarterVehicle("ACS 33");
        // Start with a two-check subset of the generic set (renumbered 1..2 by the template).
        await new VehicleFactory(context).CreateAsync(
            vehicle, _ownerId, EntrySource.Web, CheckSource.GenericStarterSet,
            selectedCheckNames: ["Walk-around: tyres, glass, wipers", "Brake fluid level"]);

        var result = await new CheckSetAdder(context).AddSetAsync(
            vehicle.Id, CheckSource.GenericStarterSet, null, null, EntrySource.Web);

        // The two it already has are skipped; the other thirteen are appended after the current max (2).
        Assert.Equal(13, result.Added.Count);
        Assert.Contains("Walk-around: tyres, glass, wipers", result.Skipped);
        Assert.Contains("Brake fluid level", result.Skipped);
        Assert.Equal([.. Enumerable.Range(3, 13)], result.Added.Select(d => d.DisplayOrder));

        Assert.Equal(15, await context.CheckDefinitions.CountAsync(d => d.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task A_retired_check_still_blocks_re_adding_its_name()
    {
        await using var context = NewContext();
        var vehicle = NewStarterVehicle("ACS 44");
        await new VehicleFactory(context).CreateAsync(vehicle, _ownerId, EntrySource.Web, CheckSource.None);

        // A retired check with a generic name — the unique index ignores IsActive, so it must still block.
        context.CheckDefinitions.Add(new CheckDefinition
        {
            VehicleId = vehicle.Id, Name = "Brake fluid level", CadenceLabel = "Monthly",
            IntervalDays = 30, DisplayOrder = 1, IsActive = false, Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var result = await new CheckSetAdder(context).AddSetAsync(
            vehicle.Id, CheckSource.GenericStarterSet, null, null, EntrySource.Web);

        Assert.Contains("Brake fluid level", result.Skipped);
        Assert.DoesNotContain(result.Added, d => d.Name == "Brake fluid level");
        Assert.Equal(14, result.Added.Count);
    }

    [Fact]
    public async Task Copying_a_set_into_an_existing_vehicle_is_active_only_and_honours_the_selection()
    {
        await using var context = NewContext();
        var sourceId = await NewVehicleAsync(context, "ACS 55"); // generic 15

        // Retire one on the source; a retired check is not copyable even if named.
        var retire = await context.CheckDefinitions
            .FirstAsync(d => d.VehicleId == sourceId && d.Name == "Air-con run, 10 minutes");
        retire.IsActive = false;
        await context.SaveChangesAsync();

        var target = NewStarterVehicle("ACS 66");
        await new VehicleFactory(context).CreateAsync(target, _ownerId, EntrySource.Web, CheckSource.None);

        var result = await new CheckSetAdder(context).AddSetAsync(
            target.Id, CheckSource.CopyFromVehicle, sourceId,
            selectedNames: ["Walk-around: tyres, glass, wipers", "Air-con run, 10 minutes"],
            EntrySource.Web);

        // Air-con is retired on the source → excluded; only the walk-around lands.
        Assert.Equal(["Walk-around: tyres, glass, wipers"], result.Added.Select(d => d.Name));
    }

    [Fact]
    public async Task The_starter_set_gives_a_new_vehicle_a_real_check_status()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "CHK 777");

        var summary = await NewMetrics(context).GetVehicleSummaryAsync(vehicleId);

        Assert.NotNull(summary);
        // 15 defined, none ever logged. Never-logged is the fourth state and the whole point of it: the
        // workbook's Dashboard counted 17 of 18 because its never-logged check fell out of every bucket.
        Assert.Equal(15, summary.Checks.TotalCount);
        Assert.Equal(15, summary.Checks.NeverLoggedCount);
        Assert.Equal(0, summary.Checks.OkCount);
        Assert.Equal(15, summary.Checks.OkCount + summary.Checks.DueSoonCount
                       + summary.Checks.OverdueCount + summary.Checks.NeverLoggedCount);
    }

    // ---- Budget: the third table with no path to existence ----------------------------------------------

    [Fact]
    public async Task A_budget_target_produces_real_variance_against_computed_actuals()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "BUD 111");

        context.BudgetCategories.Add(new BudgetCategory
        {
            VehicleId = vehicleId, Category = "Tools/Equipment", AnnualBudget = 313m, Source = EntrySource.Web,
        });
        context.ExpenseEntries.Add(new ExpenseEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 6, 8), Category = "Tools/Equipment",
            Amount = 494.95m, Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var budget = await NewMetrics(context).GetBudgetSummaryAsync(vehicleId, BudgetPeriod.CalendarYear);

        Assert.NotNull(budget);
        var line = budget.Lines.Single(l => l.Category == "Tools/Equipment");

        // The design's "tools 158% · OVER". Only the target is stored; the actual is summed at render.
        Assert.Equal(313m, line.AnnualBudget);
        Assert.Equal(494.95m, line.ActualSpend);
        Assert.True(line.IsOverBudget);
        Assert.Equal(-181.95m, line.Remaining);
    }

    [Fact]
    public async Task Spend_with_no_target_is_shown_not_hidden()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "BUD 222");

        context.ExpenseEntries.Add(new ExpenseEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 6, 8), Category = "Parking",
            Amount = 14.40m, Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var budget = await NewMetrics(context).GetBudgetSummaryAsync(vehicleId, BudgetPeriod.CalendarYear);

        Assert.NotNull(budget);
        var parking = budget.Lines.Single(l => l.Category == "Parking");

        // "Spend with no budget set — shown, never hidden". A null target is not a zero target: absent means
        // "I have not budgeted for this", and money the app knows about but does not mention is the beginning
        // of a spreadsheet that disagrees with itself.
        Assert.Null(parking.AnnualBudget);
        Assert.Equal(14.40m, parking.ActualSpend);
        Assert.Null(parking.PercentUsed);
    }

    [Fact]
    public async Task A_zero_target_is_a_real_target_not_an_absent_one()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "BUD 333");

        context.BudgetCategories.Add(new BudgetCategory
        {
            VehicleId = vehicleId, Category = "Wash", AnnualBudget = 0m, Source = EntrySource.Web,
        });
        context.ExpenseEntries.Add(new ExpenseEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 6, 30), Category = "Wash",
            Amount = 24.50m, Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var budget = await NewMetrics(context).GetBudgetSummaryAsync(vehicleId, BudgetPeriod.CalendarYear);

        Assert.NotNull(budget);
        var wash = budget.Lines.Single(l => l.Category == "Wash");

        // Zero means "spend nothing here and tell me when I do" — so £24.50 is over. Percentage is null
        // because there is no denominator, and 24.50/0 is not a number however much a screen wants one.
        Assert.Equal(0m, wash.AnnualBudget);
        Assert.True(wash.IsOverBudget);
        Assert.Null(wash.PercentUsed);
    }

    private static readonly FakeTimeProvider TestClock =
        new(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero));

    private static AnomalyScanner NewScanner(CarTrackerDbContext context) =>
        new(context, new VehicleMetricsLoader(context), TestClock);

    private static ServiceRecordFactory NewServiceFactory(CarTrackerDbContext context) =>
        new(context, new ReferenceWriter(context));

    private static DerivedMetricsService NewMetrics(CarTrackerDbContext context) =>
        new(new VehicleMetricsLoader(context),
            new Clock(new FakeTimeProvider(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero))));
}
