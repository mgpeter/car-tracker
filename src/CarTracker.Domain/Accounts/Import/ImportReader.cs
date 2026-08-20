using System.Text.Json;

namespace CarTracker.Domain.Accounts.Import;

/// <summary>How reading a file ended. Each is a different fix, so each is a different status code.</summary>
public enum ImportReadOutcome
{
    /// <summary>Parsed into a payload. Nothing is said yet about whether it is <i>coherent</i>.</summary>
    Ok = 1,

    /// <summary>Not JSON, truncated, or JSON that is not an export of this app.</summary>
    Unreadable = 2,

    /// <summary>Over the cap. Nothing was parsed and nothing was buffered past the limit.</summary>
    TooLarge = 3,
}

/// <param name="Detail">
/// What failed, in a sentence naming it. A parser message is included verbatim when there is one: "expected a
/// value at line 4001" tells someone their download was interrupted, and "could not be read" does not.
/// </param>
public sealed record ImportReadResult(ImportReadOutcome Outcome, ImportPayload? Payload, string? Detail = null)
{
    public static ImportReadResult Ok(ImportPayload payload) => new(ImportReadOutcome.Ok, payload);

    public static ImportReadResult Unreadable(string detail) =>
        new(ImportReadOutcome.Unreadable, null, detail);
}

/// <summary>
/// Turns an uploaded stream into an <see cref="ImportPayload"/>, or says why it could not.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cap is enforced while reading</b>, never from a <c>Content-Length</c> header - the rule
/// <c>DocumentStore</c> already follows for the one other upload in the app, and for the same reason: a header
/// is a claim by the client, and the point of a cap is the case where the client is wrong or lying.
/// </para>
/// <para>
/// <b>Unknown properties are ignored and that is deliberate</b>, because a file written by a later version
/// must not be refused outright - the preview warns instead. What is <i>not</i> tolerated is the mirror image:
/// <see cref="JsonSerializer"/> fills an absent member with <c>default</c> and says nothing, so a truncated
/// file deserialises into a garage full of zeroed odometers with no error anywhere. That is why parsing is
/// only half the job and <see cref="ImportValidator"/> is the other half.
/// </para>
/// <para>
/// <b>Buffered whole rather than streamed.</b> The export streams because it writes a fleet's history to a
/// network socket; this reads a capped file into memory once so that validation, the preview and a commit
/// minutes later all see the same parsed object. Twenty-five megabytes is the ceiling and the realistic figure
/// is three orders of magnitude below it.
/// </para>
/// </remarks>
public static class ImportReader
{
    /// <summary>The ceiling, the same 25 MB a document upload gets.</summary>
    public const long MaxSizeBytes = 25 * 1024 * 1024;

    public static async Task<ImportReadResult> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var buffered = await BufferCappedAsync(source, cancellationToken);
        if (buffered is null)
        {
            return new ImportReadResult(ImportReadOutcome.TooLarge, null,
                $"That file is larger than the {MaxSizeBytes / (1024 * 1024)} MB limit.");
        }

        if (buffered.Length == 0)
        {
            return ImportReadResult.Unreadable("The file is empty.");
        }

        ImportPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ImportPayload>(buffered, AccountExportService.Json);
        }
        catch (JsonException ex)
        {
            // The parser's own message, not a paraphrase of it. It names the line and the position, which is
            // the difference between "your download was interrupted" and "your file is bad somehow".
            return ImportReadResult.Unreadable($"The file is not readable JSON: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            // A shape System.Text.Json cannot construct - a string where the profile object should be, say.
            return ImportReadResult.Unreadable($"The file is JSON, but not in a shape this app can read: {ex.Message}");
        }

        if (payload is null)
        {
            return ImportReadResult.Unreadable("The file contains the JSON value null, not an account export.");
        }

        // Readable JSON is not the same thing as one of ours, and the difference has to be stated rather than
        // discovered: an unrelated document deserialises perfectly into a payload of nulls, which the empty-list
        // normalisation then turns into a cheerful "0 vehicles, nothing to do".
        if (payload.ExportedAt == default && payload.Vehicles.Count == 0)
        {
            return ImportReadResult.Unreadable(
                "This does not look like a Cambelt account export: it carries no export date and no vehicles. "
                + "Use the file that 'Download my data' produced.");
        }

        return ImportReadResult.Ok(payload);
    }

    /// <summary>Everything the stream holds, or null the moment it goes past the cap.</summary>
    /// <remarks>
    /// Returns null rather than throwing because oversize is an answer, not a fault: the endpoint turns it into
    /// a 413 and the buffer is dropped, so a caller sending a gigabyte gets one 25 MB allocation refused rather
    /// than a gigabyte accepted and then complained about.
    /// </remarks>
    private static async Task<byte[]?> BufferCappedAsync(Stream source, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];

        while (true)
        {
            var read = await source.ReadAsync(chunk, ct);
            if (read == 0) break;

            if (buffer.Length + read > MaxSizeBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        return buffer.ToArray();
    }
}
