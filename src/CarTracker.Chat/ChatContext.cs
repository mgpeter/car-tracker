using CarTracker.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CarTracker.Chat;

/// <summary>
/// The two facts that change every turn, put where they cannot cost anything: today's date, and which car is
/// on screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not in the system prompt</b>, which is frozen precisely so it can be cached — a date interpolated there
/// would rewrite the whole prefix on every request at the 1.25× write price, with no symptom but the bill.
/// Here it rides in the message body, after the breakpoint, where it costs a handful of tokens.
/// </para>
/// <para>
/// <b>Added to the last user message rather than as a message of its own.</b> The Messages API alternates roles,
/// and a second consecutive user turn is a shape worth not relying on; a message with two text blocks is
/// ordinary. It also keeps the context attached to the question it is context for.
/// </para>
/// <para>
/// The vehicle is an answer to "which car does 'it' mean", not a filter. What the assistant can see is decided
/// by the ownership filter on the DbContext, and nothing said here widens it.
/// </para>
/// </remarks>
public static class ChatContext
{
    public static void Append(IList<ChatMessage> transcript, string? vehicle, IServiceProvider services)
    {
        var last = transcript.LastOrDefault(m => m.Role == ChatRole.User);
        if (last is null) return;

        var today = services.GetRequiredService<Clock>().Today();

        var context = vehicle is { Length: > 0 }
            ? $"[context] Today is {today:d MMMM yyyy}. The vehicle on screen is {vehicle}; when the owner says "
                + "\"it\" or \"the car\" without naming one, they mean this one."
            : $"[context] Today is {today:d MMMM yyyy}. No particular vehicle is on screen — call list_vehicles "
                + "if the request could apply to more than one.";

        last.Contents.Add(new TextContent(context));
    }
}
