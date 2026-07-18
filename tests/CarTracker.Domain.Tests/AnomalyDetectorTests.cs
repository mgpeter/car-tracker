using CarTracker.Data;
using CarTracker.Domain.Tests.Workbook;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests;

public sealed class AnomalyDetectorTests
{
    /// <summary>
    /// The real history, against the real detector.
    /// </summary>
    /// <remarks>
    /// One flag: the Service History row dated 27 Jun 2026 logging 83,000 mi against a current 80,712.
    /// Nothing else — the workbook's totals are formula-computed, so litres x price agrees to the penny, and
    /// all 12 measurable intervals sit inside the plausibility band.
    /// </remarks>
    [Fact]
    public void The_workbook_raises_exactly_one_anomaly()
    {
        var found = AnomalyDetector.Detect(WorkbookFixture.Data(), existing: []);

        var anomaly = Assert.Single(found);
        Assert.Equal(AnomalyKind.MileageNonMonotonic, anomaly.Kind);
        Assert.Equal(AnomalySeverity.Error, anomaly.Severity);
        Assert.Equal(nameof(MileageReading), anomaly.EntityType);
        Assert.Contains("83,000", anomaly.Message);
    }

    [Fact]
    public void The_workbook_raises_no_fuel_anomalies()
    {
        var found = AnomalyDetector.Detect(WorkbookFixture.Data(), existing: []);

        // The sheet computes its totals, so they agree exactly; and 25.4-32.2 mpg is comfortably inside the
        // band. Both would be news if they ever changed.
        Assert.DoesNotContain(found, a => a.Kind == AnomalyKind.FuelCostDiscrepancy);
        Assert.DoesNotContain(found, a => a.Kind == AnomalyKind.ImplausibleMpg);
    }

    /// <summary>
    /// Without this, a caller that ran twice would bury the integrity screen in duplicates.
    /// </summary>
    [Fact]
    public void An_already_open_anomaly_is_not_raised_again()
    {
        var data = WorkbookFixture.Data();
        var first = AnomalyDetector.Detect(data, existing: []);

        var second = AnomalyDetector.Detect(data, existing: first);

        Assert.Empty(second);
    }

    /// <remarks>
    /// <para>
    /// Rewritten 2026-07-15, and the old name said what it asserted: "a resolved anomaly does not suppress a
    /// fresh occurrence", for all three terminal states. That reasoning holds for exactly one of them.
    /// </para>
    /// <para>
    /// The de-dup key is (Kind, EntityType, EntityId) — one fact about one row. When the owner Accepts the
    /// 83,000 mi record ("that really is what the garage wrote") the row does not change, so the detector
    /// finds the identical fact on the next scan. Re-raising it is not news; it is overruling the person who
    /// just answered. Accept would mean nothing, and every write re-scans the whole history — so the queue
    /// would refill with settled questions, which is how a warning stops being read.
    /// </para>
    /// <para>
    /// Corrected is the exception, because there the data genuinely changed. See the Corrected case below.
    /// </para>
    /// <para>
    /// This surfaced only once the detector had a production caller and a lifecycle to run against
    /// (AnomalyScanner, WritePathTests) — the write path is what made the old rule observable.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(AnomalyStatus.Accepted)]
    [InlineData(AnomalyStatus.Dismissed)]
    public void A_decision_about_an_unchanged_row_stands(AnomalyStatus status)
    {
        var data = WorkbookFixture.Data();
        var previous = AnomalyDetector.Detect(data, existing: []).ToList();

        foreach (var anomaly in previous)
        {
            anomaly.Status = status;
            anomaly.ResolvedAt = DateTimeOffset.UtcNow;
        }

        Assert.Empty(AnomalyDetector.Detect(data, previous));
    }

    [Fact]
    public void A_corrected_anomaly_does_not_suppress_a_later_one()
    {
        var data = WorkbookFixture.Data();
        var previous = AnomalyDetector.Detect(data, existing: []).ToList();

        foreach (var anomaly in previous)
        {
            anomaly.Status = AnomalyStatus.Corrected;
            anomaly.ResolvedAt = DateTimeOffset.UtcNow;
        }

        // Corrected means the value was CHANGED. If the same row is bad again it is a new fact about a
        // different number, not the question already answered — so it must be flagged. The fixture's data is
        // unchanged here, standing in for "changed, then bad again".
        Assert.Single(AnomalyDetector.Detect(data, previous));
    }

    [Fact]
    public void A_fuel_cost_discrepancy_beyond_forecourt_rounding_is_flagged()
    {
        var data = WorkbookFixture.Data() with
        {
            FuelEntries = [new FuelEntry
            {
                Id = 99,
                VehicleId = WorkbookFixture.VehicleId,
                EntryDate = new DateOnly(2026, 7, 1),
                Mileage = 80_800,
                Litres = 45m,
                PricePerLitre = 1.50m,
                // 45 x 1.50 = 67.50; a receipt of 76.50 is a transposition, not rounding.
                TotalCost = 76.50m,
                Source = EntrySource.Web,
            }],
        };

        var found = AnomalyDetector.Detect(data, existing: []);

        var anomaly = Assert.Single(found, a => a.Kind == AnomalyKind.FuelCostDiscrepancy);
        Assert.Equal(AnomalySeverity.Warning, anomaly.Severity);
        Assert.Equal(99, anomaly.EntityId);
    }

    [Fact]
    public void A_penny_of_forecourt_rounding_is_not_flagged()
    {
        var data = WorkbookFixture.Data() with
        {
            FuelEntries = [new FuelEntry
            {
                Id = 99,
                VehicleId = WorkbookFixture.VehicleId,
                EntryDate = new DateOnly(2026, 7, 1),
                Mileage = 80_800,
                Litres = 45.23m,
                PricePerLitre = 1.489m,
                // 45.23 x 1.489 = 67.3474, charged as 67.35. Real, and not an anomaly.
                TotalCost = 67.35m,
                Source = EntrySource.Web,
            }],
        };

        Assert.DoesNotContain(
            AnomalyDetector.Detect(data, existing: []),
            a => a.Kind == AnomalyKind.FuelCostDiscrepancy);
    }

    [Fact]
    public void An_implausible_mpg_is_flagged_against_the_fill_that_produced_it()
    {
        var data = WorkbookFixture.Data() with
        {
            FuelEntries =
            [
                new FuelEntry
                {
                    Id = 1, VehicleId = WorkbookFixture.VehicleId,
                    EntryDate = new DateOnly(2026, 6, 1), Mileage = 80_000,
                    Litres = 40m, PricePerLitre = 1.50m, TotalCost = 60m, Source = EntrySource.Web,
                },
                new FuelEntry
                {
                    // 300 miles on a 5 L splash: 272 mpg.
                    Id = 2, VehicleId = WorkbookFixture.VehicleId,
                    EntryDate = new DateOnly(2026, 6, 10), Mileage = 80_300,
                    Litres = 5m, PricePerLitre = 1.50m, TotalCost = 7.50m, Source = EntrySource.Web,
                },
            ],
        };

        var anomaly = Assert.Single(
            AnomalyDetector.Detect(data, existing: []),
            a => a.Kind == AnomalyKind.ImplausibleMpg);

        Assert.Equal(2, anomaly.EntityId);
        Assert.Contains("outside", anomaly.Message);
    }

    [Fact]
    public void A_clean_vehicle_raises_nothing()
    {
        var data = WorkbookFixture.Data() with
        {
            // Drop the 83,000 mi service record and the readings generated from it.
            ServiceRecords = [],
            MileageReadings = WorkbookFixture.MileageReadings()
                .Where(m => m.Mileage != 83_000)
                .ToList(),
        };

        Assert.Empty(AnomalyDetector.Detect(data, existing: []));
    }

    [Fact]
    public void Every_anomaly_is_open_and_unresolved_when_raised()
    {
        var found = AnomalyDetector.Detect(WorkbookFixture.Data(), existing: []);

        // The lifecycle constraint requires resolved_at to be null exactly when Open — a raised anomaly that
        // arrived pre-resolved would be rejected by the database.
        Assert.All(found, a =>
        {
            Assert.Equal(AnomalyStatus.Open, a.Status);
            Assert.Null(a.ResolvedAt);
        });
    }

    // ---- Reconcile: closing a flag whose cause is gone ----------------------------------------------------

    /// <summary>Drops the 83,000 mi service record and every reading it generated.</summary>
    private static VehicleMetricsData Cleaned(VehicleMetricsData data) => data with
    {
        ServiceRecords = [],
        MileageReadings = WorkbookFixture.MileageReadings()
            .Where(m => m.Mileage != 83_000)
            .ToList(),
    };

    [Fact]
    public void An_open_flag_whose_cause_is_gone_reconciles()
    {
        var data = WorkbookFixture.Data();
        var raised = AnomalyDetector.Detect(data, existing: []).ToList();

        // Delete the culprit: the 83,000 mi reading is no longer in the data, so its condition is no longer
        // true. The flag it raised is now pointing at a row that is gone.
        var toResolve = AnomalyDetector.Reconcile(Cleaned(data), existing: raised);

        var flag = Assert.Single(toResolve);
        Assert.Equal(AnomalyKind.MileageNonMonotonic, flag.Kind);
        Assert.Same(raised[0], flag); // the very flag, to be transitioned in place — not a copy
    }

    [Fact]
    public void An_open_flag_whose_condition_still_holds_does_not_reconcile()
    {
        var data = WorkbookFixture.Data();
        var raised = AnomalyDetector.Detect(data, existing: []).ToList();

        // Same data: the 83,000 row is still there, so the condition still holds. Reconcile must leave it — a
        // flag Detect would suppress as still-present is exactly a flag Reconcile leaves alone.
        Assert.Empty(AnomalyDetector.Reconcile(data, existing: raised));
    }

    [Theory]
    [InlineData(AnomalyStatus.Accepted)]
    [InlineData(AnomalyStatus.Dismissed)]
    public void A_decided_flag_is_never_auto_resolved_even_when_its_cause_vanishes(AnomalyStatus status)
    {
        var data = WorkbookFixture.Data();
        var raised = AnomalyDetector.Detect(data, existing: []).ToList();

        // The owner judged it — "that really is what the garage wrote" (Accepted) or "not worth it" (Dismissed).
        foreach (var anomaly in raised)
        {
            anomaly.Status = status;
            anomaly.ResolvedAt = DateTimeOffset.UtcNow;
        }

        // Even with the cause gone, a decision is not the scanner's to revisit. Auto-resolving here would
        // overrule the owner with a rule — the same line the re-raise suppression holds.
        Assert.Empty(AnomalyDetector.Reconcile(Cleaned(data), existing: raised));
    }

    [Fact]
    public void A_no_op_scan_reconciles_nothing()
    {
        // Clean data, nothing on file: there is neither a condition to find nor a flag to retract.
        Assert.Empty(AnomalyDetector.Reconcile(Cleaned(WorkbookFixture.Data()), existing: []));
    }
}
