using CarTracker.Data;

namespace CarTracker.Domain;

/// <summary>
/// Resolves the account a new reference-list row belongs to, refusing loudly when there is not one.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Garage"/>, <see cref="WashLocation"/> and <see cref="ExpenseCategory"/> are keyed
/// <c>(OwnerId, Name)</c>, so an insert with no owner is not a row missing a field — it is a row with half a
/// primary key. EF refuses it with <i>"the value of 'Garage.OwnerId' is unknown"</i>, which names the column and
/// not the cause; the caller sees a 500 and learns nothing about which request had no account behind it.
/// </para>
/// <para>
/// The two no-owner states are separate bugs and say so in separate sentences. <b>No request context at all</b>
/// (a background job, a design-time tool, a directly constructed test context) means the code path is wrong: a
/// reference row belongs to an account and a system context has none to give it. <b>A request that resolved no
/// account</b> (an API-key or anonymous principal, an unprovisioned identity) means the pipeline is wrong: the
/// request reached a write it should never have been authorised for. Telling them apart is the difference
/// between fixing a caller and fixing the middleware.
/// </para>
/// </remarks>
internal static class ReferenceOwner
{
    /// <summary>The signed-in account's id, or a diagnosis of why there is not one.</summary>
    /// <param name="what">The row being created, named as the message reads it: "garage", "wash location".</param>
    public static int Require(ICurrentUserAccessor currentUser, string what) => currentUser switch
    {
        { OwnerId: int ownerId } => ownerId,

        { BypassOwnership: true } => throw new InvalidOperationException(
            $"Cannot create a {what}: this context has no signed-in account (a background, design-time or " +
            "directly constructed context bypasses ownership). Reference lists belong to an account, so the " +
            "caller must run under one — in tests, build the context with an accessor pinned to a seeded owner."),

        _ => throw new InvalidOperationException(
            $"Cannot create a {what}: the request resolved no account. Only an Auth0 principal or an assistant " +
            "token carries one; an API-key or anonymous principal must never reach a reference-list write."),
    };
}
