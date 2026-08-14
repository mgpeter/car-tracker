using Microsoft.Extensions.Configuration;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The credential these tests need, and the decision to skip when it is absent.
/// </summary>
/// <remarks>
/// <para>
/// Everything in this project calls the real Messages API, which costs real money and needs a key CI does not
/// have. So a missing key is a <b>skip</b>, not a failure: `dotnet test` stays green on a fresh checkout and on
/// the build server, and the same command runs the live assertions on a machine that has the key. A live test
/// that fails without a credential trains people to ignore red.
/// </para>
/// <para>
/// The key is read from the WebApi's user-secrets store <b>by id</b>, so a dev machine holds it in exactly one
/// place and no test project needs its own copy. Calling <c>AddUserSecrets</c> explicitly is unconditional —
/// unlike the host's default configuration, which only adds it in Development, and which is why
/// <c>ASPNETCORE_ENVIRONMENT</c> has produced three fake bugs in this repo.
/// </para>
/// </remarks>
internal static class LiveModel
{
    /// <summary>The WebApi's <c>UserSecretsId</c>. One store, one key, whichever project asks for it.</summary>
    private const string WebApiUserSecretsId = "cartracker-webapi-0001";

    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddUserSecrets(WebApiUserSecretsId)
        .AddEnvironmentVariables()
        .Build();

    /// <summary>
    /// The API key, or null when this machine has none. <c>Chat:ApiKey</c> is what the app itself reads;
    /// <c>ANTHROPIC_API_KEY</c> is honoured as a fallback because it is what the SDK and the CLI use.
    /// </summary>
    public static string? ApiKey =>
        Configuration["Chat:ApiKey"] is { Length: > 0 } fromSecrets
            ? fromSecrets
            : Configuration["ANTHROPIC_API_KEY"] is { Length: > 0 } fromEnvironment
                ? fromEnvironment
                : null;

    /// <summary>
    /// The model under measurement. Configurable so the Sonnet/Opus comparison (task 8.1) is a setting rather
    /// than an edit, and defaulting to the cheaper candidate so an accidental run is the cheap one.
    /// </summary>
    public static string Model => Configuration["Chat:Model"] ?? "claude-sonnet-5";
}

/// <summary>A <see cref="FactAttribute"/> that skips itself when no key is configured.</summary>
internal sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (LiveModel.ApiKey is null)
        {
            Skip = "No Chat:ApiKey (or ANTHROPIC_API_KEY) configured. These tests call the live Messages API "
                + "and cost money; set the secret to run them.";
        }
    }
}
