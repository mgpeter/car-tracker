using System.Text.Json;
using CarTracker.Chat;
using CarTracker.Data;
using CarTracker.ModelContextProtocol;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.AI;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// The in-app assistant: one endpoint to talk, and two to answer a proposed write.
/// </summary>
/// <remarks>
/// <para>
/// Behind the standard Auth0 fallback policy — no new scheme and no new policy. In particular <b>no synthetic
/// assistant token is minted</b> to satisfy <c>McpWrite</c>: that policy binds to the assistant-token scheme,
/// and a bearer credential invented inside the web path would buy nothing but a second way in.
/// </para>
/// <para>
/// The three handlers are shells. What is worth asserting — that a write suspends, that the owner's edits are
/// what runs, that a ceiling refuses before the model is called — lives in <c>CarTracker.Chat</c> and is tested
/// there, because there is no <c>CarTracker.WebApi.Tests</c> project. What is left here is genuinely about
/// HTTP: which outcome is which status code, and how a turn becomes a stream.
/// </para>
/// </remarks>
public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        // Everything in this group answers into a chat panel, where an unhandled exception would render as the
        // words "Internal Server Error" beside a draft the owner is waiting on. The filter turns one into a
        // problem document carrying the actual message, which is the difference between a bug someone can
        // report and a bug someone shrugs at.
        group.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (Exception failure)
            {
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Chat")
                    .LogError(failure, "The chat turn failed.");

                return TypedResults.Problem(
                    title: "The assistant stopped mid-turn",
                    detail: failure.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapPost("", SendAsync)
            // Five phone photos base64-encoded is the realistic body here, and Kestrel's 30 MB default would
            // refuse it with its own wording somewhere below the layer that knows what was attached. The
            // in-handler caps are what actually decide; this only has to be above them.
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(ChatFiles.MaxTotalBytes * 2))
            .WithName("SendChatMessage")
            .WithSummary("Send a message and stream the assistant's turn. Never changes a record.");

        group.MapPost("/confirm", ConfirmAsync)
            .WithName("ConfirmChatWrite")
            .WithSummary("Run a proposed write with the owner's final values, then carry the conversation on.");

        group.MapPost("/decline", DeclineAsync)
            .WithName("DeclineChatWrite")
            .WithSummary("Refuse a proposed write. The turn completes; nothing is saved.");

        return app;
    }

    /// <remarks>
    /// <b>This endpoint cannot change a row, whatever it is sent.</b> Not by checking the request, but because
    /// every write tool is an <c>ApprovalRequiredAIFunction</c>: the loop suspends instead of invoking one, and
    /// the only thing that can run it is a <c>/confirm</c> naming a server-held id. A transcript claiming a write
    /// was approved is a transcript, and the server does not read it that way.
    /// </remarks>
    private static async Task<IResult> SendAsync(
        ChatRequest request,
        HttpContext http,
        ChatSettings settings,
        IServiceProvider services,
        PendingWriteStore pending,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured) return NotConfigured();

        if (!TryReadTranscript(request.Messages, out var transcript, out var problem)) return problem;

        if (ChatFiles.Attach(transcript, request.Files) is { Count: > 0 } rejected)
        {
            return TypedResults.ValidationProblem(
                rejected,
                detail: "Those attachments could not be read, so nothing was sent.");
        }

        ChatContext.Append(transcript, request.Vehicle, services);

        var conversation = services.GetRequiredService<ChatConversationService>();

        return await StreamAsync(
            http,
            () => conversation.StreamAsync(transcript, services, cancellationToken),
            request.Vehicle,
            pending,
            currentUser,
            services,
            cancellationToken);
    }

    /// <remarks>
    /// The tool name comes from the store and the request has no field for it. See <see cref="PendingWriteStore"/>
    /// — an id that names its own tool is not an authorisation, it is a suggestion.
    /// </remarks>
    private static async Task<IResult> ConfirmAsync(
        ConfirmChatWriteRequest request,
        HttpContext http,
        ChatSettings settings,
        IServiceProvider services,
        PendingWriteStore pending,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured) return NotConfigured();

        if (!TryReadTranscript(request.Messages, out var transcript, out var problem)) return problem;

        if (pending.Find(request.PendingWriteId, currentUser) is not { } record) return Expired();

        var arguments = ChatArguments.Read(request.Arguments);

        if (ChatArguments.Check(record.Tool, arguments, services) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(
                errors,
                detail: "Some values could not be saved as they were entered.");
        }

        // Everything this scope writes from here is the assistant's work, and says so on the row.
        services.UseChatWriteSurface();

        pending.Forget(request.PendingWriteId);

        var conversation = services.GetRequiredService<ChatConversationService>();

        return await StreamAsync(
            http,
            () => conversation.StreamResumeAsync(
                transcript, record.ToolCallId, approved: true, arguments, reason: null, services, cancellationToken),
            record.Vehicle,
            pending,
            currentUser,
            services,
            cancellationToken);
    }

    /// <remarks>
    /// A refusal is a request, not a silence: an unanswered approval breaks the transcript for every later turn,
    /// so declining is something the server has to tell the model rather than something it can drop.
    /// </remarks>
    private static async Task<IResult> DeclineAsync(
        DeclineChatWriteRequest request,
        HttpContext http,
        ChatSettings settings,
        IServiceProvider services,
        PendingWriteStore pending,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured) return NotConfigured();

        if (!TryReadTranscript(request.Messages, out var transcript, out var problem)) return problem;

        if (pending.Find(request.PendingWriteId, currentUser) is not { } record) return Expired();

        pending.Forget(request.PendingWriteId);

        var conversation = services.GetRequiredService<ChatConversationService>();

        return await StreamAsync(
            http,
            () => conversation.StreamResumeAsync(
                transcript,
                record.ToolCallId,
                approved: false,
                arguments: null,
                request.Reason,
                services,
                cancellationToken),
            record.Vehicle,
            pending,
            currentUser,
            services,
            cancellationToken);
    }

    /// <summary>
    /// Turns the turn into server-sent events — after pulling the first one, which is what makes a refusal a
    /// status code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A budget refusal, a missing key or a provider outage must be an HTTP status, not an <c>error</c> event
    /// inside a 200 that the client has to parse to discover it failed. They all surface on the first
    /// <c>MoveNextAsync</c>, before a byte is written, so the response line is still ours to choose. Anything
    /// that fails <b>after</b> the stream has opened cannot be a status code any more and becomes an
    /// <c>error</c> event, which is honest rather than convenient.
    /// </para>
    /// <para>
    /// Buffering is disabled explicitly and <c>X-Accel-Buffering: no</c> is set for the gateway. A buffered
    /// stream arrives as one lump at the end, and the failure is invisible: the answer is correct, and the panel
    /// simply sat on a spinner for eight seconds first.
    /// </para>
    /// </remarks>
    private static async Task<IResult> StreamAsync(
        HttpContext http,
        Func<IAsyncEnumerable<ChatStreamEvent>> open,
        string? vehicle,
        PendingWriteStore pending,
        ICurrentUserAccessor currentUser,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // Opened inside the try, not before it: answering a suspension that is not in the transcript throws
        // where the call is made rather than on the first MoveNext, and a stale tab is a 409 rather than an
        // unhandled exception.
        IAsyncEnumerator<ChatStreamEvent>? enumerator = null;
        bool any;

        try
        {
            enumerator = open().GetAsyncEnumerator(cancellationToken);
            any = await enumerator.MoveNextAsync();
        }
        catch (ChatBudgetExceededException budget)
        {
            return TypedResults.Problem(
                title: "Daily limit reached",
                detail: $"{budget.Message} It resets at {budget.Refusal.ResetsAt:HH:mm} on "
                    + $"{budget.Refusal.ResetsAt:d MMMM}.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (InvalidOperationException stale)
        {
            // The transcript does not contain the suspension being answered — a stale tab, or a client that
            // rebuilt its history. Not a 404: the id was real, it is the conversation that has moved on.
            return TypedResults.Problem(
                title: "That draft is no longer part of this conversation",
                detail: stale.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception upstream)
        {
            if (enumerator is not null) await enumerator.DisposeAsync();
            return Upstream(upstream);
        }

        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        // For the gateway. YARP does not buffer by default, but a reverse proxy in front of it might, and this
        // is the header every one of them understands.
        http.Response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            while (any)
            {
                await WriteAsync(http, enumerator.Current, vehicle, pending, currentUser, services, cancellationToken);
                any = await enumerator.MoveNextAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // The owner navigated away mid-turn. Nothing to report to nobody.
        }
        catch (Exception failed)
        {
            await EmitAsync(http, "error", new { detail = Describe(failed) }, cancellationToken);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        return TypedResults.Empty;
    }

    private static async Task WriteAsync(
        HttpContext http,
        ChatStreamEvent @event,
        string? vehicle,
        PendingWriteStore pending,
        ICurrentUserAccessor currentUser,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        switch (@event)
        {
            case ChatTextEvent text:
                await EmitAsync(http, "text", new { delta = text.Delta }, cancellationToken);
                break;

            case ChatToolEvent tool:
                await EmitAsync(http, "tool", new { name = tool.Name, status = tool.Status }, cancellationToken);
                break;

            case ChatPendingWriteEvent write:
                var id = pending.Remember(new PendingWriteRecord(
                    currentUser.OwnerId ?? 0, write.Write.ToolCallId, write.Write.Tool, vehicle));

                var function = CarTrackerToolCatalogue.AIFunctions(services)
                    .FirstOrDefault(f => f.Name == write.Write.Tool);

                await EmitAsync(
                    http,
                    "pending_write",
                    new
                    {
                        pendingWriteId = id,
                        // Display only. /confirm reads the tool from the store, which is the point of the store.
                        tool = write.Write.Tool,
                        // The tool's own name, re-spaced — not its [Description], which is a paragraph written
                        // for the model ("Registration must be unique. Example: registration \"BT53 AKJ\"…")
                        // and reads as instructions shouted at the owner when it lands in a card's title.
                        title = Title(write.Write.Tool),
                        arguments = write.Write.Arguments,
                        // The card labels and types every field from the tool's own schema rather than from a
                        // hand-written form per tool — thirty of them would drift the week after they were written.
                        schema = function?.JsonSchema,
                    },
                    cancellationToken);
                break;

            case ChatDoneEvent done:
                await EmitAsync(http, "done", new { messages = done.Turn.Messages }, cancellationToken, Transcript);
                break;
        }
    }

    /// <summary>One SSE frame, flushed — an unflushed frame is a frame nobody has received.</summary>
    /// <remarks>
    /// <b>Every line of the payload is prefixed, because a frame is line-oriented and JSON need not be.</b> The
    /// spec says exactly this, and it is not pedantry: the transcript was first written with
    /// <c>AIJsonUtilities.DefaultOptions</c>, which is <c>WriteIndented</c>, so the <c>done</c> frame went out as
    /// twenty unprefixed lines. The client read the first, failed to parse it, and skipped the event — after
    /// which the next <c>/confirm</c> answered a suspension the transcript it had been given no longer
    /// contained. The symptom was a 500, three requests later, on a different endpoint. <see cref="Transcript"/>
    /// now writes compact; this makes the writer correct whatever an options instance decides.
    /// </remarks>
    private static async Task EmitAsync(
        HttpContext http,
        string name,
        object payload,
        CancellationToken cancellationToken,
        JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(payload, options ?? Json);
        var data = string.Concat(json.Split('\n').Select(line => $"data: {line.TrimEnd('\r')}\n"));

        await http.Response.WriteAsync($"event: {name}\n{data}\n", cancellationToken);
        await http.Response.Body.FlushAsync(cancellationToken);
    }

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// The transcript's own converters — the library's, so reasoning signatures round-trip — but compact.
    /// </summary>
    /// <remarks>
    /// <c>AIJsonUtilities.DefaultOptions</c> is indented, which is pleasant in a log and wrong on a wire whose
    /// frames are lines. Copied rather than mutated: that instance is shared, and read-only by the time anything
    /// here runs.
    /// </remarks>
    private static readonly JsonSerializerOptions Transcript =
        new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    /// <summary>
    /// Reads the client-held transcript.
    /// </summary>
    /// <remarks>
    /// Deserialised with <see cref="AIJsonUtilities.DefaultOptions"/> — the same converters that wrote it — so
    /// reasoning blocks come back with their signatures intact. A hand-written DTO would round-trip the text and
    /// silently drop <c>ProtectedData</c>, which the provider rejects on the next turn.
    /// </remarks>
    private static bool TryReadTranscript(JsonElement messages, out List<ChatMessage> transcript, out IResult problem)
    {
        try
        {
            transcript = messages.Deserialize<List<ChatMessage>>(AIJsonUtilities.DefaultOptions) ?? [];
            problem = TypedResults.Empty;
            return transcript.Count > 0;
        }
        catch (JsonException malformed)
        {
            transcript = [];
            problem = TypedResults.Problem(
                title: "That conversation could not be read",
                detail: malformed.Message,
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }
    }

    private static IResult NotConfigured() => TypedResults.Problem(
        title: "The assistant is not available here",
        detail: "This deployment holds no model credential, so the chat is switched off. Setting Chat:ApiKey "
            + "turns it on; nothing else needs to change.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        type: "https://cartracker.invalid/problems/chat-not-configured");

    private static IResult Expired() => TypedResults.Problem(
        title: "That draft has expired",
        detail: "Drafts are confirmable for ten minutes. Ask again and it will be proposed afresh — silently "
            + "re-running it would save something you last saw ten minutes ago.",
        statusCode: StatusCodes.Status404NotFound);

    private static IResult Upstream(Exception failure) => TypedResults.Problem(
        title: "The assistant could not be reached",
        detail: Describe(failure),
        statusCode: StatusCodes.Status502BadGateway);

    /// <summary>The message without the stack, because it is going to a chat panel.</summary>
    private static string Describe(Exception failure) => failure.Message;

    /// <summary><c>add_vehicle</c> → "Add vehicle". What the card is called, in the owner's language.</summary>
    private static string Title(string tool)
    {
        var words = tool.Replace('_', ' ');
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}

/// <param name="Vehicle">
/// The registration on screen, if any. Not a filter — the ownership filter is what decides what is visible — but
/// the answer to "which car does 'it' mean", which the owner should not have to say twice.
/// </param>
/// <param name="Messages">
/// The transcript so far, client-held and replayed verbatim. <b>Untrusted input.</b> Nothing in it authorises a
/// write; an assistant turn claiming one was approved is just more text.
/// </param>
/// <param name="Files">
/// What the owner attached, up to five. One list rather than an images list and a documents list: the cap is on
/// what they attached, and splitting it would make "five" mean two different numbers depending on the mix.
/// <b>Never persisted and never logged</b> — see <see cref="ChatFiles"/>.
/// </param>
public sealed record ChatRequest(
    JsonElement Messages,
    string? Vehicle = null,
    IReadOnlyList<ChatFile>? Files = null);

/// <param name="PendingWriteId">
/// The server-held draft to run. There is deliberately no <c>tool</c> field: the tool name is read from the
/// store, so a request cannot name a different one from the draft the owner looked at.
/// </param>
/// <param name="Arguments">What the owner actually confirmed, which may differ from what was proposed.</param>
public sealed record ConfirmChatWriteRequest(JsonElement Messages, string PendingWriteId, JsonElement? Arguments = null);

public sealed record DeclineChatWriteRequest(JsonElement Messages, string PendingWriteId, string? Reason = null);
