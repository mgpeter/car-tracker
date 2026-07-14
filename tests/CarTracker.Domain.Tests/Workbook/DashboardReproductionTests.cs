using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests.Workbook;

/// <summary>
/// Task 5.5: reproduce every Dashboard figure the sheet got right, and pin the ones it did not.
/// </summary>
/// <remarks>
/// <para>
/// Dashboard values are quoted from the sheet itself (sheet: Dashboard, rows 3-16).
/// </para>
/// <para>
/// <b>The sheet's day counts were computed at serial 46214 = 2026-07-11</b>, not the 2026-07-14 reference
/// date — Excel's TODAY() froze when the file was last saved. So "Days to MOT = 26" and "Days to insurance =
/// 247" are correct for 11 July and three days stale at the reference date. The expiry <i>dates</i> are what
/// this suite compares; the day counts are recomputed.
/// </para>
/// </remarks>
public sealed class DashboardReproductionTests
{
    private static VehicleSummary Summary() =>
        DerivedMetrics.Compute(WorkbookFixture.Data(), WorkbookFixture.ReferenceDate);

    // ---- Figures the sheet got right, reproduced exactly -------------------------------------------------

    [Fact]
    public void Total_spend_ytd_matches_the_dashboard()
    {
        // Dashboard row 3: 5146.71 — the sum of all 17 expense rows.
        Assert.Equal(5_146.71m, Summary().Spend.TotalYtd);
    }

    [Fact]
    public void Service_and_repairs_ytd_matches_the_dashboard()
    {
        // Dashboard row 5: 603.99 = Service 570 + Parts 13.99 + Parts 20.
        Assert.Equal(603.99m, Summary().Spend.ServiceAndRepairsYtd);
    }

    [Fact]
    public void Statutory_ytd_matches_the_dashboard()
    {
        // Dashboard row 6: 1005.14 = Insurance 517.14 + Tax 430 + MOT 58.
        Assert.Equal(1_005.14m, Summary().Spend.StatutoryYtd);
    }

    [Fact]
    public void Total_spend_since_purchase_matches_the_dashboard()
    {
        // Dashboard row 7: 5146.71. Every expense post-dates the 14 Mar purchase, so it equals YTD.
        Assert.Equal(5_146.71m, Summary().Spend.TotalSincePurchase);
    }

    [Fact]
    public void Latest_logged_mileage_matches_the_dashboard()
    {
        // Dashboard row 5: 80712. The sheet knows the right number — it just does not use it (row 6).
        Assert.Equal(80_712, Summary().Mileage.CurrentMileage);
    }

    [Fact]
    public void Best_mpg_matches_the_dashboard()
    {
        // Dashboard row 12: 32.153092337917485 — fill 4 (1 May, 78,403 mi).
        Assert.Equal(32.1531m, Math.Round(Summary().Fuel.BestMpg!.Value, 4));
    }

    [Fact]
    public void Last_fill_date_matches_the_dashboard()
    {
        // Dashboard row 16: serial 46213.
        Assert.Equal(new DateOnly(2026, 7, 10), Summary().Fuel.LastFillDate);
    }

    [Fact]
    public void Check_counts_would_match_the_dashboard_shape()
    {
        // Dashboard rows 22-23: 7 overdue, 3 due soon, 7 OK — 17 of 18, because "Spare tyre pressure" has
        // never been logged and the sheet has nowhere to put it. The Regular Checks sheet is not transcribed
        // here (the four defects do not need it), so this pins the arithmetic rather than the data.
        var summary = new CheckStatusSummary(OkCount: 7, DueSoonCount: 3, OverdueCount: 7, NeverLoggedCount: 1, Checks: []);

        Assert.Equal(18, summary.TotalCount);
        Assert.Equal(17, summary.OkCount + summary.DueSoonCount + summary.OverdueCount);
    }

    // ---- Figures that differ, and why -------------------------------------------------------------------

    /// <summary>
    /// Finding 5: average price per litre is a <b>definition difference</b>, not a defect.
    /// </summary>
    /// <remarks>
    /// The sheet takes a plain mean of the price column (20.734 / 13 = 1.594923). This service weights by
    /// volume (888.86 / 556.47 = 1.597324) — the answer to "what did fuel actually cost me per litre". A 50 L
    /// fill at £1.40 and a 10 L fill at £1.60 average to £1.433, not £1.50. Predicted by the spec, which said
    /// to report it rather than silently resolve it either way.
    /// </remarks>
    [Fact]
    public void Average_price_per_litre_is_volume_weighted_and_differs_from_the_dashboard()
    {
        const decimal dashboardSimpleMean = 1.5949230769230771m;
        var computed = Summary().Fuel.AveragePricePerLitre!.Value;

        Assert.Equal(1.597324m, Math.Round(computed, 6));
        Assert.NotEqual(Math.Round(dashboardSimpleMean, 4), Math.Round(computed, 4));

        // Small in absolute terms — 0.24p/L — but it is a different question, not a rounding artefact.
        Assert.Equal(0.0024m, Math.Round(computed - dashboardSimpleMean, 4));
    }

    /// <summary>
    /// Finding 6: the sheet computes MPG for the <b>first</b> fill, which has no predecessor.
    /// </summary>
    /// <remarks>
    /// Fuel Log row 4 carries "miles since last = 334" against a mileage of 77,537 — implying a previous
    /// reading of 77,203 that exists nowhere in the workbook (the purchase was at 76,632). That invented
    /// interval yields 24.49 MPG, which the sheet then reports as <b>Worst MPG</b> (row 13) and folds into
    /// its 13-value Average (row 11).
    ///
    /// This service measures 12 intervals from 13 fills, so both figures differ. The sheet's are built on a
    /// number with no basis in its own data.
    /// </remarks>
    [Fact]
    public void Average_and_worst_mpg_differ_because_the_sheet_invents_an_interval_for_the_first_fill()
    {
        var fuel = Summary().Fuel;

        // 13 fills, 12 measurable intervals.
        Assert.Equal(13, fuel.FillCount);
        Assert.Equal(12, fuel.ReliableIntervalCount);

        // Dashboard row 13 reports 24.490226774193548 as Worst MPG — the first fill's invented interval.
        // Ours is the worst of the 12 real ones.
        Assert.Equal(25.4225m, Math.Round(fuel.WorstMpg!.Value, 4));
        Assert.NotEqual(24.4902m, Math.Round(fuel.WorstMpg.Value, 4));

        // Dashboard row 11 reports 28.775700675674550, a mean over 13. Ours is over 12.
        Assert.NotEqual(28.7757m, Math.Round(fuel.AverageMpg!.Value, 4));
    }

    /// <summary>
    /// The sheet's cost-per-mile inherits the manual-mileage defect.
    /// </summary>
    [Fact]
    public void Cost_per_mile_differs_because_the_sheet_divides_by_the_manual_mileage()
    {
        var summary = Summary();

        // Dashboard row 8: 1.2636164988951633 = 5146.71 / 4073, using the manual 80,705.
        const decimal dashboardCostPerMile = 1.2636164988951633m;

        // Ours divides by the derived 4,080.
        Assert.Equal(Math.Round(5_146.71m / 4_080m, 6), Math.Round(summary.Spend.CostPerMile!.Value, 6));
        Assert.NotEqual(Math.Round(dashboardCostPerMile, 6), Math.Round(summary.Spend.CostPerMile.Value, 6));
    }
}
