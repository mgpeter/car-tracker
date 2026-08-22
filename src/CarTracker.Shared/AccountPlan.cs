namespace CarTracker.Shared;

/// <summary>What an account is allowed to spend.</summary>
/// <remarks>
/// <para>
/// Two members, because there are two answers to give today and inventing a tier nothing can reach would be a
/// guess dressed as a design. <b>The enum is not stored anywhere</b> - it is derived on every request from the
/// comp list and the account's verified address, so there is no column to fall out of step with the truth and
/// no migration when the way of earning <see cref="Pro"/> changes.
/// </para>
/// <para>
/// <b>It lives in Shared rather than beside the rest of the plan machinery</b> because <c>User.PlanOverride</c>
/// stores it (DEC-023), and <c>CarTracker.Data</c> cannot reference <c>CarTracker.Domain</c> - the reference
/// runs the other way. Same reason <see cref="EntrySource"/> and <see cref="MileageOrigin"/> are here: an enum
/// an entity holds is a shared vocabulary, not a domain rule. <c>PlanReason</c>, <c>PlanResolution</c> and
/// <c>PlanAllowances</c> stay in the domain, because nothing stores them.
/// </para>
/// <para>
/// When checkout lands, an active subscription becomes the second way to be <see cref="Pro"/> and this enum
/// does not move.
/// </para>
/// </remarks>
public enum AccountPlan
{
    /// <summary>The default, and what every unknown person gets. Bounded on all three costly surfaces.</summary>
    Free = 0,

    /// <summary>Comped today, subscribed later. The assistant, and headroom on the other two.</summary>
    Pro = 1,
}
