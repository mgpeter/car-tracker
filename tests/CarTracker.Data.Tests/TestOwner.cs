using CarTracker.Data;
using CarTracker.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Data.Tests;

/// <summary>
/// Provisions a <see cref="User"/> to own test vehicles and reference rows. Multi-user made an owner mandatory
/// on every vehicle created through <see cref="CarTracker.Domain.VehicleFactory"/>, so a test that creates one
/// provisions an owner first and passes its id.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent by external id: xUnit runs a class's <c>InitializeAsync</c> before <b>every</b> test and the test
/// database is not reset between them, so a plain insert would hit the unique <c>external_id</c> index on the
/// second test. Find-or-create returns the same owner each time.
/// </para>
/// <para>
/// It also creates the 13 system expense categories, because that is what an account <i>is</i> now: the
/// categories stopped being migration seed data when they gained an owner, so a user row without them is a
/// state production will never produce and every expense write would refuse.
/// </para>
/// </remarks>
internal static class TestOwner
{
    /// <summary>
    /// An accessor pinned to <paramref name="ownerId"/> — a signed-in request, for a context or a write path
    /// that must behave like one.
    /// </summary>
    /// <remarks>
    /// The one way to build an owned context, because the alternative is a false green: a context constructed
    /// with no accessor has <c>BypassOwnership</c>, which makes every ownership predicate — the query filters
    /// and the correlated <c>Vehicles.Any()</c> the reference-list statements are scoped by — match every row.
    /// An isolation test written that way passes without isolating anything.
    /// </remarks>
    public static CurrentUserAccessor As(int ownerId)
    {
        var accessor = new CurrentUserAccessor();
        accessor.SetOwner(ownerId);
        return accessor;
    }

    public static async Task<int> SeedAsync(CarTrackerDbContext context, string externalId = "test|owner")
    {
        var existing = await context.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId);
        if (existing is not null) return existing.Id;

        var user = new User
        {
            ExternalId = externalId,
            Email = $"{externalId.Replace('|', '.')}@example.test",
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        context.Users.Add(user);

        // Two saves, not one: User.Id is store-generated and the category's owner FK is navigation-less, so
        // nothing would fill it in before the insert. The categories need the id the first save produces.
        await context.SaveChangesAsync();

        // The same seam CurrentUserMiddleware provisions through, so a test account and a real one are the same
        // thing — and it projects fresh entities rather than handing back the process-wide singletons.
        context.ExpenseCategories.AddRange(ExpenseCategoryProvisioner.ForNewUser(user));
        await context.SaveChangesAsync();

        return user.Id;
    }
}
