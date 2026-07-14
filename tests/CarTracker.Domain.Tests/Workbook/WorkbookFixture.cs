using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests.Workbook;

/// <summary>
/// The real BT53 AKJ history, transcribed by hand from
/// <c>archive/ORIGINAL-TRACKER-IN-EXCEL-Freelander_BT53AKJ_Tracker.xlsx</c>.
/// </summary>
/// <remarks>
/// <para>
/// DEC-008 removed the importer, so nothing reads the workbook programmatically. These figures were read out
/// of it once and written down here; the file remains the source of truth. Every block cites its sheet and
/// row so a disputed number can be checked in seconds.
/// </para>
/// <para>
/// Dates in the workbook are Excel serials, epoch 1899-12-30 (46217 = 2026-07-14). They are written here as
/// real dates, with the serial in a comment where it aids checking.
/// </para>
/// </remarks>
public static class WorkbookFixture
{
    /// <summary>Every figure in the spec is stated at this date.</summary>
    public static readonly DateOnly ReferenceDate = new(2026, 7, 14);

    public const int VehicleId = 1;

    /// <summary>Sheet: Vehicle Info. Purchased 14 Mar 2026 at 76,632 mi.</summary>
    public static Vehicle Vehicle() => new()
    {
        Id = VehicleId,
        Registration = "BT53 AKJ",
        Make = "Land Rover",
        Model = "Freelander 1",
        Variant = "1.8 SE Station Wagon",
        Year = 2003,
        Colour = "Navy Blue",
        PurchaseDate = new DateOnly(2026, 3, 14),
        PurchaseMileage = 76_632,
        PurchasePrice = 1_700m,
        FuelType = FuelType.Petrol,
        EngineCode = "K-series",
        // Dashboard row 9 stores MOT expiry as serial 46240 = 2026-08-06. It is seeded here only to prove
        // the derived value overrides it — see WorkbookValidationTests.
        MotExpirySeed = new DateOnly(2026, 8, 6),
        VedExpiry = new DateOnly(2027, 2, 28),   // Dashboard row 13, serial 46446
        Insurance = new InsurancePolicy
        {
            Insurer = "Admiral / EUI Ltd",
            PeriodEnd = new DateOnly(2027, 3, 15), // Dashboard row 11, serial 46461
            Premium = 517.14m,
        },
        Source = EntrySource.Import,
    };

    /// <summary>
    /// Sheet: Fuel Log, rows 4-16. Thirteen fills.
    /// </summary>
    /// <remarks>
    /// <b>Fill level is invented, not transcribed.</b> The Fuel Log has no fill-level column — its
    /// "Full tank / Half / Quarter" columns hold computed range-per-tank estimates (329 / 164.5 / 82.25 =
    /// x1, x0.5, x0.25), not the enum README §2 assumes. Every fill is recorded as Full because the observed
    /// MPGs are all plausible (24-32) and nothing suggests a splash-and-dash. This is the one place the
    /// fixture asserts something the workbook does not say.
    /// </remarks>
    public static List<FuelEntry> FuelEntries() =>
    [
        //      id  date          serial  mileage  litres  price/L  total
        Fill(1, "2026-04-18", 46130, 77_537, 62.00m, 1.589m, 98.518m),
        Fill(2, "2026-04-18", 46130, 77_770, 36.78m, 1.589m, 58.44342m),
        Fill(3, "2026-04-21", 46133, 78_079, 45.81m, 1.569m, 71.87589m),
        Fill(4, "2026-05-01", 46143, 78_403, 45.81m, 1.569m, 71.87589m),
        Fill(5, "2026-05-03", 46145, 78_513, 18.74m, 1.575m, 29.5155m),
        Fill(6, "2026-05-13", 46155, 78_846, 50.65m, 1.579m, 79.97635m),
        Fill(7, "2026-05-17", 46159, 79_031, 27.21m, 1.579m, 42.96459m),
        Fill(8, "2026-05-23", 46165, 79_316, 42.82m, 1.579m, 67.61278m),
        Fill(9, "2026-05-30", 46172, 79_630, 47.32m, 1.659m, 78.50388m),
        Fill(10, "2026-06-06", 46179, 79_911, 45.43m, 1.620m, 73.5966m),
        Fill(11, "2026-06-14", 46187, 80_197, 48.38m, 1.499m, 72.52162m),
        Fill(12, "2026-06-23", 46196, 80_449, 38.49m, 1.529m, 58.85121m),
        Fill(13, "2026-07-10", 46213, 80_712, 47.03m, 1.799m, 84.60697m),
    ];

    /// <summary>
    /// Sheet: Expenses Log, rows 3-19. Seventeen populated rows.
    /// </summary>
    /// <remarks>
    /// Row 10 (15 May, £453.17) is the <b>lumped fuel row</b>: one "fuel to date" entry instead of per-fill
    /// mirroring. It is transcribed as-is because this fixture reproduces the workbook, defects included —
    /// the tests then show what it costs.
    /// </remarks>
    public static List<ExpenseEntry> Expenses() =>
    [
        Expense("2026-03-14", "Purchase", 1_700m, "Lee (private)", 76_632),
        Expense("2026-03-14", "Insurance", 517.14m, "Admiral / EUI Ltd", 76_632),
        Expense("2026-03-14", "Tax", 430m, "DVLA", 76_632),
        Expense("2026-03-14", "Tools/Equipment", 500m, "Amazon, Euro Car Parts", 76_632),
        Expense("2026-04-20", "Parking", 440m, "Surbiton Station Car Park", 77_000),
        Expense("2026-05-12", "Service", 570m, "K & P Motors Kingston", 78_840),
        Expense("2026-05-12", "Parts", 13.99m, "eBay", 78_840),
        Expense("2026-05-15", "Fuel", 453.17m, "Esso", 79_031),   // <- the lumped row
        Expense("2026-05-20", "Wash", 24m, "Power Foam", 79_031),
        Expense("2026-05-23", "Fuel", 67.61m, "Esso", 79_316),
        Expense("2026-06-06", "Fuel", 73.55m, "Texaco Cotswolds", 79_911),
        Expense("2026-06-14", "Fuel", 72.52m, "Random Peak District station", 80_197),
        Expense("2026-06-19", "Tools/Equipment", 131.88m, "Amazon", 80_400),
        Expense("2026-06-19", "Parts", 20m, "JGS4x4, eBay", 80_400),
        Expense("2026-06-23", "Fuel", 58.85m, "Esso", 80_449),
        Expense("2026-06-30", "Wash", 16m, "Power Foam", 80_450),
        Expense("2026-07-08", "MOT", 58m, "K & P Motors Kingston", 80_705),
    ];

    /// <summary>
    /// Sheet: Service History, rows 3-8. Six records.
    /// </summary>
    /// <remarks>
    /// <b>Type is normalised.</b> The sheet says "MOT Test"; the schema requires exactly "MOT", because that
    /// literal is how derived MOT expiry is found. The importer used to normalise this — DEC-008 removed it,
    /// so the fixture does what a writer must now do.
    /// </remarks>
    public static List<ServiceRecord> ServiceRecords() =>
    [
        // 2025 MOT, pre-ownership. Its next-due (6 Aug 2026) is exactly what the Dashboard still shows.
        Service("2025-07-17", 76_632, "MOT", nextDueDate: "2026-08-06"),
        Service("2026-03-14", 76_632, "Pre-purchase check"),
        Service("2026-03-20", 76_700, "Hand-brake adjustment"),
        Service("2026-05-12", 78_840, "Major service", cost: 511.80m, nextDueMileage: 87_500, nextDueDate: "2027-05-12"),
        // The known-bad row: 83,000 mi on 27 Jun, above the current 80,712. Likely 80,300 mistyped.
        Service("2026-06-27", 83_000, "Reverse gear switch replacement"),
        // The MOT that makes the Dashboard's stored expiry stale.
        Service("2026-07-08", 80_705, "MOT", cost: 58m, nextDueDate: "2027-07-08"),
    ];

    /// <summary>
    /// Mileage readings, generated from the logs the way the app would generate them.
    /// </summary>
    /// <remarks>
    /// The workbook has no mileage log — `MileageReading` exists precisely to decouple current mileage from
    /// any one sheet. Every fill and service contributes one, plus the manual 80,705 the Vehicle Info sheet
    /// states (Dashboard row 4, "Current mileage (manual)").
    /// </remarks>
    public static List<MileageReading> MileageReadings()
    {
        var readings = FuelEntries()
            .Select(f => Reading(f.EntryDate, f.Mileage, MileageOrigin.Fuel))
            .Concat(ServiceRecords().Select(s => Reading(s.ServiceDate, s.Mileage, MileageOrigin.Service)))
            .ToList();

        // Dashboard row 4: the manual figure, behind the logs. This is the whole reason current mileage is
        // derived rather than typed.
        readings.Add(Reading(new DateOnly(2026, 7, 8), 80_705, MileageOrigin.Manual));

        return readings;
    }

    /// <param name="serial">
    /// The Excel serial the date came from. Carried so a transcription can be re-checked against the file
    /// without recomputing epochs by hand — my first pass converted these a month out.
    /// </param>
    private static FuelEntry Fill(int id, string date, int serial, int mileage, decimal litres, decimal pricePerLitre, decimal total)
    {
        AssertSerialMatches(date, serial);

        return new FuelEntry
        {
            Id = id,
            VehicleId = VehicleId,
            EntryDate = DateOnly.Parse(date),
            Mileage = mileage,
            Litres = litres,
            PricePerLitre = pricePerLitre,
            TotalCost = total,
            FillLevel = FillLevel.Full,
            Source = EntrySource.Import,
        };
    }

    /// <summary>Excel serial epoch is 1899-12-30. Anchor: 46217 = 2026-07-14.</summary>
    private static readonly DateOnly SerialEpoch = new(1899, 12, 30);

    private static void AssertSerialMatches(string date, int serial)
    {
        var fromSerial = SerialEpoch.AddDays(serial);
        var parsed = DateOnly.Parse(date);

        if (fromSerial != parsed)
        {
            throw new InvalidOperationException(
                $"Fixture transcription error: serial {serial} is {fromSerial:yyyy-MM-dd}, not {parsed:yyyy-MM-dd}. " +
                "Check against archive/ORIGINAL-TRACKER-IN-EXCEL-Freelander_BT53AKJ_Tracker.xlsx.");
        }
    }

    private static ExpenseEntry Expense(string date, string category, decimal amount, string vendor, int mileage) =>
        new()
        {
            VehicleId = VehicleId,
            EntryDate = DateOnly.Parse(date),
            Category = category,
            Amount = amount,
            Vendor = vendor,
            Mileage = mileage,
            Source = EntrySource.Import,
        };

    private static ServiceRecord Service(
        string date,
        int mileage,
        string type,
        decimal? cost = null,
        int? nextDueMileage = null,
        string? nextDueDate = null) =>
        new()
        {
            VehicleId = VehicleId,
            ServiceDate = DateOnly.Parse(date),
            Mileage = mileage,
            Type = type,
            Cost = cost,
            NextDueMileage = nextDueMileage,
            NextDueDate = nextDueDate is null ? null : DateOnly.Parse(nextDueDate),
            Source = EntrySource.Import,
        };

    private static MileageReading Reading(DateOnly date, int mileage, MileageOrigin origin) =>
        new()
        {
            VehicleId = VehicleId,
            ReadingDate = date,
            Mileage = mileage,
            Origin = origin,
            Source = EntrySource.Import,
        };

    public static VehicleMetricsData Data() => new(
        Vehicle(),
        MileageReadings(),
        FuelEntries(),
        Expenses(),
        ServiceRecords(),
        CheckDefinitions: [],
        CheckLogs: [],
        BudgetCategories: []);
}
