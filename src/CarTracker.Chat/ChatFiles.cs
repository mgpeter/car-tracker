using Microsoft.Extensions.AI;

namespace CarTracker.Chat;

/// <summary>One thing the owner attached: a photograph, a scan, a certificate.</summary>
/// <param name="MediaType">What it is. The server maps it to an image or a document block from this alone.</param>
/// <param name="Data">Base64, no newlines.</param>
public sealed record ChatFile(string MediaType, string Data);

/// <summary>
/// Attaching what the owner photographed to the message they sent with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is persisted or logged.</b> A file reaches the model and the response is prose; the bytes are
/// never written to the documents volume, never named in a log line, and never survive the request. Filing a
/// certificate is a separate, deliberate act on the documents screen — the chat reads paperwork, it does not
/// collect it.
/// </para>
/// <para>
/// <b>The accepted list is shorter than <c>DocumentStore.AllowedContentTypes</c>, and that is not an oversight.</b>
/// The documents screen stores bytes it never has to understand, so it accepts HEIC and GIF happily. These are
/// sent to a model to be *read*, and the list is what the provider can actually see. HEIC in particular is
/// converted in the browser before it gets here — which is why a phone can attach one and this list still cannot.
/// Do not "fix" either list to match the other.
/// </para>
/// </remarks>
public static class ChatFiles
{
    /// <summary>
    /// The cap is on what the owner attached, not per kind — splitting images from documents would make "five"
    /// mean two different numbers depending on the mix.
    /// </summary>
    public const int MaxFiles = 5;

    /// <summary>Comfortably a phone photo or a scanned certificate; small enough that five cannot be a denial of service.</summary>
    public const int MaxBytesPerFile = 10 * 1024 * 1024;

    public const int MaxTotalBytes = 20 * 1024 * 1024;

    /// <summary>What a model can be asked to read. See the class remarks before widening it.</summary>
    public static IReadOnlySet<string> Readable { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/pdf",
    };

    /// <summary>
    /// Attaches the files to the last user message, or returns why it would not.
    /// </summary>
    /// <returns>
    /// An RFC 9457 <c>errors</c> map, empty when everything attached. Keyed by <c>files[n]</c> so a client that
    /// shows five thumbnails can mark the one that was refused rather than refusing all five.
    /// </returns>
    public static Dictionary<string, string[]> Attach(IList<ChatMessage> transcript, IReadOnlyList<ChatFile>? files)
    {
        Dictionary<string, string[]> errors = [];

        if (files is null or { Count: 0 }) return errors;

        if (files.Count > MaxFiles)
        {
            errors["files"] = [$"Up to {MaxFiles} files at a time, and this message has {files.Count}."];
            return errors;
        }

        var last = transcript.LastOrDefault(m => m.Role == ChatRole.User);

        if (last is null)
        {
            errors["files"] = ["There is no message to attach these to."];
            return errors;
        }

        List<DataContent> attachments = [];
        var total = 0L;

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var key = $"files[{i}]";

            if (!Readable.Contains(file.MediaType))
            {
                errors[key] = [
                    $"'{file.MediaType}' is not something the assistant can read. Send a JPEG, PNG, WebP or PDF."];
                continue;
            }

            byte[] bytes;

            try
            {
                bytes = Convert.FromBase64String(file.Data);
            }
            catch (FormatException)
            {
                errors[key] = ["That file did not arrive intact. Try attaching it again."];
                continue;
            }

            if (bytes.Length > MaxBytesPerFile)
            {
                errors[key] = [
                    $"That file is {bytes.Length / (1024 * 1024)} MB and the limit is {MaxBytesPerFile / (1024 * 1024)} MB."];
                continue;
            }

            total += bytes.Length;

            if (total > MaxTotalBytes)
            {
                errors[key] = [
                    $"Together these come to more than {MaxTotalBytes / (1024 * 1024)} MB. Send them in two messages."];
                continue;
            }

            attachments.Add(new DataContent(bytes, file.MediaType));
        }

        // Nothing is attached unless everything was accepted: a turn that quietly read three of five files would
        // answer confidently about paperwork it never saw.
        if (errors.Count > 0) return errors;

        foreach (var attachment in attachments) last.Contents.Add(attachment);

        return errors;
    }
}
