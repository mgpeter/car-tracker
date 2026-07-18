namespace CarTracker.Shared;

/// <summary>
/// How full the tank was after a fill. Load-bearing for MPG grouping: <see cref="Full"/> (or an unrecorded,
/// null level) <b>closes the tank</b> and lets the fill measure MPG across the segment since the last closing
/// fill; <see cref="Half"/>/<see cref="Quarter"/> mark a <b>partial</b> whose figure is deferred to the next
/// fill to full, with its litres accumulated into that span. Only "closes vs not" is read — Half and Quarter
/// are treated identically.
/// </summary>
public enum FillLevel
{
    Full = 1,
    Half = 2,
    Quarter = 3,
}
