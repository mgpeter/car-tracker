using CarTracker.Data;
using CarTracker.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The chat wired the way the app wires it, for the tests that talk to a real model.
/// </summary>
/// <remarks>
/// Built through <see cref="ChatServiceCollectionExtensions.AddCarTrackerChat"/> rather than by newing the
/// service up, so the registration is part of what these tests cover: the per-scope pipeline, the settings
/// binding and the provider seam are exactly the ones that ship. A hand-assembled service would keep passing
/// after the registration stopped matching it.
/// </remarks>
internal static class LiveChat
{
    /// <summary>
    /// A request scope. Resolve <see cref="ChatConversationService"/> from it and pass the same
    /// <c>scope.ServiceProvider</c> as the tools' provider — that pairing is the ownership boundary.
    /// </summary>
    public static IServiceScope NewScope()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Chat:ApiKey"] = LiveModel.ApiKey,
                ["Chat:Model"] = LiveModel.Model,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCarTrackerDomain();
        // Present so the tools' DbContext parameter is a dependency rather than a published argument; never
        // connected to, because these tests are about the model and not the database.
        services.AddDbContext<CarTrackerDbContext>(o => o.UseNpgsql("Host=localhost;Database=none"));
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddSingleton(TimeProvider.System);
        services.AddCarTrackerChat(configuration);

        // Overrides the real ledger, which reads a database these tests do not have — and which would refuse
        // every turn here anyway, correctly: no request pipeline has resolved an owner, and an unattributable
        // turn is one nobody is accountable for. The ledger itself is asserted against a real database in
        // `ChatBudgetTests`; what these tests are about is the model.
        services.AddScoped<IChatBudget>(_ => new FakeBudget());

        return services.BuildServiceProvider().CreateScope();
    }
}
