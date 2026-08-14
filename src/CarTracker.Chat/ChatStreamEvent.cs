namespace CarTracker.Chat;

/// <summary>
/// What a turn emits as it happens. The endpoint renders these as server-sent events.
/// </summary>
/// <remarks>
/// A closed set rather than a stream of provider updates: the client is a chat panel, not a model client, and
/// everything it needs is here. It also keeps the provider's own update shape out of the wire contract, which
/// is the same reason the tools are wrapped rather than exposed.
/// </remarks>
public abstract record ChatStreamEvent;

/// <summary>Assistant prose, incremental.</summary>
public sealed record ChatTextEvent(string Delta) : ChatStreamEvent;

/// <summary>
/// A <b>read</b> tool ran on its own, surfaced so the panel can say what it is doing.
/// </summary>
/// <remarks>
/// Never a write: a write suspends instead of running, and arrives as <see cref="ChatPendingWriteEvent"/>. This
/// is narration, not something to act on — which is why it carries a status and no result.
/// </remarks>
public sealed record ChatToolEvent(string Name, string Status) : ChatStreamEvent;

/// <summary>The loop stopped and is waiting for the owner. Render the draft card.</summary>
public sealed record ChatPendingWriteEvent(PendingWrite Write) : ChatStreamEvent;

/// <summary>The turn is over. Carries the authoritative transcript to keep client-side.</summary>
public sealed record ChatDoneEvent(ChatTurn Turn) : ChatStreamEvent;
