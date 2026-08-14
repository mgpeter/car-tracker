using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CarTracker.Chat.Tests;

/// <summary>
/// What an unfilled deployment file does — which is not the same question as what a filled one does.
/// </summary>
/// <remarks>
/// <c>deploy/docker-compose.yml</c> writes every key it knows about, so a variable nobody set arrives as an
/// <b>empty string, not an absent key</b>. That distinction has cost this project a release before, in another
/// costume, and here it has two edges: an empty string binds to a plain <c>long</c> by throwing, which takes the
/// application down at boot; and it binds to a <c>string</c> perfectly, replacing the shipped model id with "".
/// </remarks>
public sealed class ChatSettingsTests
{
    private static ChatSettings Bind(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddCarTrackerChat(configuration);

        return services.BuildServiceProvider().GetRequiredService<ChatSettings>();
    }

    [Fact]
    public void An_env_file_with_every_key_left_blank_still_boots_on_the_defaults()
    {
        var settings = Bind(
            ("Chat:ApiKey", ""),
            ("Chat:Model", ""),
            ("Chat:DailyTokensPerOwner", ""),
            ("Chat:DailyTokensGlobal", ""));

        Assert.False(settings.IsConfigured);
        Assert.Equal(ChatSettings.DefaultModel, settings.Model);
        Assert.Equal(ChatSettings.DefaultDailyTokensPerOwner, settings.PerOwnerCeiling);
        Assert.Equal(ChatSettings.DefaultDailyTokensGlobal, settings.GlobalCeiling);
    }

    [Fact]
    public void An_explicit_zero_is_off_and_a_blank_is_not()
    {
        // The third polarity in the deployment file, and the one most likely to be misread: blank is generous,
        // zero is closed. Both are asserted together because the pair is what makes either one legible.
        Assert.Equal(0, Bind(("Chat:DailyTokensPerOwner", "0")).PerOwnerCeiling);
        Assert.Equal(ChatSettings.DefaultDailyTokensPerOwner, Bind(("Chat:DailyTokensPerOwner", "")).PerOwnerCeiling);
    }

    [Fact]
    public void A_configured_deployment_reads_what_it_was_given()
    {
        var settings = Bind(
            ("Chat:ApiKey", "sk-test"),
            ("Chat:Model", "claude-opus-5"),
            ("Chat:DailyTokensPerOwner", "250000"));

        Assert.True(settings.IsConfigured);
        Assert.Equal("claude-opus-5", settings.Model);
        Assert.Equal(250_000, settings.PerOwnerCeiling);
    }

    [Fact]
    public void The_reason_the_allowances_are_nullable()
    {
        // Not about this feature — about the binder, and it is here so that someone tidying `long?` back to
        // `long` learns what it costs from a red test rather than from a container that will not start.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Limit"] = string.Empty })
            .Build();

        Assert.Throws<InvalidOperationException>(() => configuration.Bind(new PlainLong()));

        var nullable = new NullableLong { Limit = 5 };
        configuration.Bind(nullable);
        Assert.Null(nullable.Limit);
    }

    private sealed class PlainLong
    {
        public long Limit { get; set; }
    }

    private sealed class NullableLong
    {
        public long? Limit { get; set; }
    }
}
