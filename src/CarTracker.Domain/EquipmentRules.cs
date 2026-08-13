using CarTracker.Shared;

namespace CarTracker.Domain;

/// <summary>
/// The one place the line is drawn between kit you have paid for and kit you have only priced.
/// </summary>
/// <remarks>
/// <para>
/// Four things branch on this — the write-path refusal, the expense mirror, the mirror's reconcile on edit,
/// and <see cref="AnomalyDetector"/>'s <see cref="AnomalyKind.EquipmentCostWithoutDate"/>. Each of them read
/// only <c>(Cost, PurchasedDate)</c> before, and the result was wrong in both directions at once:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>A shopping-list item could not be recorded at all.</b> "Tow rope, £40, to order" was refused, because
/// the guard demanded a purchase date for any cost — so the one status whose entire purpose is pricing
/// something before you buy it was the one you could not price.
/// </item>
/// <item>
/// <b>And an unbought item's estimate reached the budget.</b> The mirror fired on any cost with a date, and
/// the add sheet pre-filled today's date, so a £40 estimate became a real <c>Tools/Equipment</c> expense in
/// spend, cost-per-mile and the Equipment &amp; Tools group.
/// </item>
/// </list>
/// <para>
/// The equipment screen's "Kit value · owned items with a cost" tile had the rule right the whole time; this
/// makes the domain agree with the only place it was ever stated.
/// </para>
/// </remarks>
public static class EquipmentRules
{
    /// <summary>
    /// Whether an item's cost is money spent rather than an estimate on a shopping list.
    /// </summary>
    /// <remarks>
    /// Written as "not <see cref="EquipmentStatus.ToOrder"/>" rather than as a list of the two statuses that
    /// do count, so a fourth status added later defaults to <em>counting</em> — the failure mode of a new
    /// status silently dropping money out of every total is worse than the failure mode of it appearing in a
    /// total someone then has to correct. Absent money is invisible; present money is arguable.
    /// </remarks>
    public static bool CostIsSpend(EquipmentStatus status) => status != EquipmentStatus.ToOrder;
}
