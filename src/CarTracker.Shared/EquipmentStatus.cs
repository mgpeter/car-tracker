namespace CarTracker.Shared;

/// <summary>
/// Where a piece of kit is between wanting it and having it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This enum decides whether an item's cost is money.</b> It had no documentation at all while its meaning
/// lived only in a comment on the equipment screen — which was tolerable while nothing in the domain branched
/// on it, and stopped being tolerable the moment the expense mirror did. See
/// <c>CarTracker.Domain.EquipmentRules.CostIsSpend</c>, which is the one place the line is drawn.
/// </para>
/// </remarks>
public enum EquipmentStatus
{
    /// <summary>
    /// You have it. The cost is money that left your account, so it mirrors into
    /// <c>Tools/Equipment</c> and needs a purchase date to sit in the right month.
    /// </summary>
    Owned = 1,

    /// <summary>
    /// Bought and on its way. Paid for but not yet in the car — the money has still gone, so it counts exactly
    /// like <see cref="Owned"/> and needs the date it was ordered.
    /// </summary>
    OnOrder = 2,

    /// <summary>
    /// On the shopping list. A cost here is an <b>estimate of what it will cost</b>, not a payment — so it
    /// needs no purchase date, mirrors into no expense, reaches no budget, and is not flagged for lacking one.
    /// Pricing something you intend to buy is the point of the list.
    /// </summary>
    ToOrder = 3,
}
