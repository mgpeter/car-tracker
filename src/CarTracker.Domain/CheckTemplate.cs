using CarTracker.Data;

namespace CarTracker.Domain;

/// <summary>
/// Where a new vehicle's regular checks come from.
/// </summary>
/// <remarks>
/// The roadmap's add-car flow: empty, a generic starter set, or copied from a car you already have.
/// </remarks>
public enum CheckSource
{
    /// <summary>No checks. The checks screen says so, and Settings is where you add them.</summary>
    None = 0,

    /// <summary>The fifteen checks that apply to any car.</summary>
    GenericStarterSet = 1,

    /// <summary>Every active definition from another vehicle, cadences and all.</summary>
    CopyFromVehicle = 2,
}

/// <summary>
/// The generic starter set.
/// </summary>
/// <remarks>
/// <para>
/// Fifteen checks, and deliberately <b>not</b> BT53 AKJ's eighteen. Three of that car's checks are not generic
/// at all — they are the reason it has them:
/// </para>
/// <list type="bullet">
///   <item><description>"Oil filler cap underside" and "Coolant reservoir colour &amp; level" are the K-series
///   head-gasket early-warning system. On a car without a K-series they are noise.</description></item>
///   <item><description>"VCU one-wheel-up rotation test" is a Freelander viscous coupling. Most cars have no
///   VCU to test.</description></item>
/// </list>
/// <para>
/// Shipping those to every vehicle would be the same mistake as a seeded vehicle: presenting one car's
/// specifics as everyone's defaults. They are added per-vehicle, which is what the definitions CRUD is for.
/// </para>
/// <para>
/// A code template rather than <c>HasData</c> seeding, because <see cref="CheckDefinition"/> is vehicle-scoped
/// with a unique index on (VehicleId, Name) — there is no vehicle to hang a seed off, and DEC-007 forbids
/// seeding anything vehicle-scoped anyway. It is applied at create time, then owned by the vehicle: edit or
/// delete them freely, and nothing re-applies this list.
/// </para>
/// </remarks>
public static class CheckTemplate
{
    /// <param name="Name">Unique per vehicle — the database enforces it.</param>
    /// <param name="CadenceLabel">What the checks screen shows. Prose, so "3–4 weekly" is expressible.</param>
    /// <param name="IntervalDays">
    /// What the status actually derives from. Due-soon is the last 20% of the interval
    /// (<c>CheckStatusCalculator</c>), so a range like the wash window's 21–28 days is approximated by its
    /// outer bound: 28 here warns from day 22 rather than the design's day 21. A day, and the alternative is
    /// a second interval column that only one check would use.
    /// </param>
    public sealed record Item(string Name, string CadenceLabel, int IntervalDays, string? Guidance = null);

    /// <summary>Ordered as they should appear: weekly first, then monthly, then the long intervals.</summary>
    public static readonly IReadOnlyList<Item> Generic =
    [
        new("Walk-around: tyres, glass, wipers", "Weekly", 7, "visual — cuts, stone damage, perishing"),
        new("Exterior lights & indicators", "Weekly", 7),
        new("Under-car drip scan", "Weekly", 7, "oil or coolant spots where it stands overnight"),
        new("Engine oil level", "Monthly", 30, "cold engine, level surface"),
        new("Tyre pressures, all 4 + spare", "Monthly", 30),
        new("Spare tyre pressure", "With tyres", 30, "a flat spare is no spare"),
        new("Battery terminals", "Monthly", 30, "corrosion, tightness, clamp state"),
        new("Under-bonnet scan", "Monthly", 30, "hoses, belts, leaks, perished rubber"),
        new("Exterior lights — full check", "Monthly", 30, "all functions incl. fogs and reverse"),
        new("Air-con run, 10 minutes", "Monthly", 30, "keeps seals lubricated"),
        new("Wiper blades & washers", "Monthly", 30),
        new("Brake fluid level", "Monthly", 30),
        new("Power steering fluid", "Monthly", 30),
        new("Wash & underbody rinse", "3–4 weekly", 28, "rust defence — sills, arches, hinges"),
        new("Tread depth, all 4 tyres", "Quarterly", 90, "1.6 mm legal, 3 mm advisory"),
    ];

    /// <param name="selectedNames">
    /// When non-null, only the generic checks whose <see cref="Item.Name"/> is in this set are produced — the
    /// add-car toggle selection. Template order is preserved and <c>DisplayOrder</c> renumbered contiguously
    /// over the kept subset, so a partial selection has no gaps. <c>null</c> means the whole set (the default,
    /// unchanged); an empty set means none.
    /// </param>
    internal static IEnumerable<CheckDefinition> For(int vehicleId, IReadOnlyCollection<string>? selectedNames = null) =>
        (selectedNames is null ? Generic : Generic.Where(item => selectedNames.Contains(item.Name)))
        .Select((item, index) => new CheckDefinition
        {
            VehicleId = vehicleId,
            Name = item.Name,
            CadenceLabel = item.CadenceLabel,
            IntervalDays = item.IntervalDays,
            Guidance = item.Guidance,
            DisplayOrder = index + 1,
            IsActive = true,
        });
}
