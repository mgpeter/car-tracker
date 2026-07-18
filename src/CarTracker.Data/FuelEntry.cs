using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// One fill-up. No MPG, L/100km or miles-since-last columns — all three derive from this row and its
/// predecessor, and storing them is what doubled the workbook's litres total.
/// </summary>
/// <remarks>
/// <see cref="TotalCost"/> alongside litres × price is deliberate and is not a stored derived value: it is
/// transcribed from the receipt, which is authoritative, and forecourt rounding routinely makes the product
/// differ by a penny. The write path flags a discrepancy over 2p as an anomaly; no constraint enforces
/// agreement because that would reject legitimate receipts.
/// </remarks>
public class FuelEntry : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public DateOnly EntryDate { get; set; }

    public int Mileage { get; set; }

    public decimal Litres { get; set; }

    public decimal PricePerLitre { get; set; }

    public decimal TotalCost { get; set; }

    public string? Station { get; set; }

    /// <summary>
    /// Whether this fill closed the tank, which drives MPG grouping in <c>FuelEconomyCalculator</c>.
    /// </summary>
    /// <remarks>
    /// Full or unrecorded (null) <b>closes the tank</b>: the fill measures MPG across the segment since the last
    /// closing fill. Half/Quarter mark a <b>partial</b> — MPG is deferred to the next fill to full, and this
    /// fill's litres accumulate into that measured span (nothing is discarded). Only "closes vs not" is read;
    /// Half vs Quarter is never read arithmetically. Nullable because the source data does not always record it,
    /// and null is treated as closing — the add-fill sheet defaults to Full, so an untouched field asserts the
    /// normal, filled-to-full case.
    /// </remarks>
    public FillLevel? FillLevel { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
