namespace CarTracker.Data;

/// <summary>
/// One <see cref="CheckDefinition"/> that is part of an <see cref="Issue"/>'s early-warning watch — the link
/// that makes "resolved, contingent on these checks staying current" expressible.
/// </summary>
/// <remarks>
/// <para>
/// The motivating case is BT53's K-series head gasket: the issue is resolved off a compression test and a CO₂
/// sniff, and the weekly oil-filler-cap and coolant-colour checks are what keep it resolved. Without this row
/// the app can only say "7 checks overdue" and cannot say which two of them are the early-warning system for a
/// known frailty — a comment in <c>VehicleCard.tsx</c> has read "nothing models WHICH checks are the
/// head-gasket watch" since the garage screen was ported.
/// </para>
/// <para>
/// Structural, like <see cref="BudgetGroupCategory"/>: no audit block, because the row is a statement about a
/// pair rather than an event with a source. A composite key rather than a surrogate id — the pair *is* the
/// identity, and it makes the link idempotent.
/// </para>
/// <para>
/// The watch's <b>status</b> is deliberately absent. It is the live status of these checks, which
/// <c>CheckStatusCalculator</c> already computes; storing "lapsed" here would be a second answer to a question
/// that already has one (DEC-002).
/// </para>
/// </remarks>
public class IssueWatchCheck
{
    public int IssueId { get; set; }

    public int CheckDefinitionId { get; set; }
}
