using CarTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Data.Tests;

/// <summary>
/// Seeds a <see cref="User"/> to own test vehicles. Multi-user made an owner mandatory on every vehicle created
/// through <see cref="CarTracker.Domain.VehicleFactory"/>, so a test that creates one seeds an owner first and
/// passes its id.
/// </summary>
/// <remarks>
/// Idempotent by external id: xUnit runs a class's <c>InitializeAsync</c> before <b>every</b> test and the test
/// database is not reset between them, so a plain insert would hit the unique <c>external_id</c> index on the
/// second test. Find-or-create returns the same owner each time.
/// </remarks>
internal static class TestOwner
{
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
        await context.SaveChangesAsync();
        return user.Id;
    }
}
