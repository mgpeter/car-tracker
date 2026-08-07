using System.Security.Cryptography;

namespace CarTracker.Domain.Documents;

/// <summary>Where the bytes live. Resolved to an absolute path by the host at registration.</summary>
/// <remarks>
/// A record rather than <c>IOptions&lt;T&gt;</c> so the domain does not take a dependency on the options or
/// hosting stack for one string — the WebApi reads <c>Documents:RootPath</c> and hands the resolved path in.
/// </remarks>
public sealed record DocumentStorageOptions(string RootPath);

/// <param name="AlreadyExisted">
/// True when a byte-identical file was already on the volume. The name is deliberately not "IsDuplicate": the
/// store reports what it found on disk, and whether that constitutes a duplicate *for this vehicle* is a
/// question about rows, which the endpoint answers.
/// </param>
public sealed record StoredFile(string RelativePath, string Sha256, long SizeBytes, bool AlreadyExisted);

/// <summary>
/// Writes uploaded bytes to the mounted volume and reads them back (DEC-005 — files on disk, path in the DB;
/// <c>bytea</c> bloats <c>pg_dump</c> and MinIO is a third container for one user, both rejected).
/// </summary>
/// <remarks>
/// <para>
/// <b>Content-addressed.</b> The file is named for the SHA-256 of its own bytes, under a per-vehicle folder. Two
/// receipts both called <c>scan.pdf</c> cannot collide, a client-supplied filename never becomes a path
/// component (which is how directory traversal gets in), and a byte-identical re-upload resolves to the file
/// already there instead of a second copy.
/// </para>
/// <para>
/// <b>Hashed while streaming, in one pass.</b> Not by re-reading the file afterwards: the hash is needed to name
/// the file, and reading a 20 MB scan twice to learn what we just wrote would be work for nothing. The bytes go
/// to a temp file through a <see cref="CryptoStream"/> and are moved into place once the name is known.
/// </para>
/// </remarks>
public sealed class DocumentStore(DocumentStorageOptions options)
{
    /// <summary>
    /// What may be uploaded, and the extension each lands on disk with. PDFs and photos, which is what the
    /// design promises ("PDF or photos") and what a viewer can honestly render.
    /// </summary>
    /// <remarks>
    /// An allow-list, not a deny-list: the set of things that are safe to serve back to a browser is small and
    /// known, and the set that is dangerous is neither. The extension exists so the Phase 5 folder-copy backup
    /// is browsable by a human — nothing in the app reads it, since <see cref="Data.Document.ContentType"/> is
    /// what the download sets.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> AllowedContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/heic"] = ".heic",
            ["image/heif"] = ".heif",
            ["image/gif"] = ".gif",
        };

    /// <summary>
    /// The largest file accepted. Generous for a phone photo or a scanned multi-page certificate, and small
    /// enough that a mistake cannot fill the volume before anyone notices.
    /// </summary>
    public const long MaxSizeBytes = 25 * 1024 * 1024;

    public static bool IsAllowed(string? contentType) =>
        contentType is not null && AllowedContentTypes.ContainsKey(contentType);

    /// <summary>
    /// Streams <paramref name="source"/> to the volume, hashing as it goes. Returns null when the stream exceeds
    /// <see cref="MaxSizeBytes"/> — the partial file is removed, so an oversize upload leaves nothing behind.
    /// </summary>
    /// <remarks>
    /// The cap is enforced <b>while reading</b> rather than from a Content-Length header: a header is a claim by
    /// the client, and the point of a cap is the case where the client is wrong or lying.
    /// </remarks>
    public async Task<StoredFile?> SaveAsync(
        int vehicleId, Stream source, string contentType, CancellationToken cancellationToken = default)
    {
        var vehicleRoot = Path.Combine(options.RootPath, vehicleId.ToString());
        Directory.CreateDirectory(vehicleRoot);

        var temp = Path.Combine(vehicleRoot, $".upload-{Guid.NewGuid():N}.tmp");
        long size;
        byte[] hash;

        try
        {
            await using (var file = File.Create(temp))
            using (var sha = SHA256.Create())
            await using (var crypto = new CryptoStream(file, sha, CryptoStreamMode.Write))
            {
                size = await CopyCappedAsync(source, crypto, cancellationToken);
                // Finalise either way: the CryptoStream owns the file handle, and disposing it un-finalised
                // throws over the top of whatever we were actually trying to report.
                await crypto.FlushFinalBlockAsync(cancellationToken);
                hash = sha.Hash!;
            }

            if (size < 0)
            {
                File.Delete(temp);
                return null;
            }

            var digest = Convert.ToHexStringLower(hash);
            var extension = AllowedContentTypes.TryGetValue(contentType, out var ext) ? ext : string.Empty;
            var name = $"{digest}{extension}";
            var destination = Path.Combine(vehicleRoot, name);

            // Same bytes already on the volume: keep the one that is there. Overwriting would be a no-op with a
            // window where the file does not exist, and the existing row's path still has to resolve.
            var existed = File.Exists(destination);
            if (existed) File.Delete(temp);
            else File.Move(temp, destination);

            return new StoredFile(
                RelativePath: Path.Combine(vehicleId.ToString(), name).Replace('\\', '/'),
                Sha256: digest,
                SizeBytes: size,
                AlreadyExisted: existed);
        }
        catch
        {
            // A failed upload must not leave a temp file behind — the volume is not self-cleaning.
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
    }

    /// <summary>Copies up to the cap, returning the byte count — or -1 the moment the cap is exceeded.</summary>
    private static async Task<long> CopyCappedAsync(Stream source, Stream destination, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxSizeBytes) return -1;
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    /// <summary>
    /// Opens the stored bytes for reading, or null when the row points at a file that is no longer there.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception because the case is real and recoverable: file storage is not transactional
    /// with the database (DEC-005 names this as its cost), so a restored `pg_dump` without its volume, or a
    /// half-finished backup, produces rows whose bytes are missing. The endpoint turns that into a 404 that says
    /// so, which is more use than a 500.
    /// </remarks>
    public Stream? OpenRead(string relativePath)
    {
        var full = Resolve(relativePath);
        return full is not null && File.Exists(full) ? File.OpenRead(full) : null;
    }

    /// <summary>
    /// Removes the bytes, if no other row still points at them. Returns true when a file was actually deleted.
    /// </summary>
    /// <remarks>
    /// <paramref name="stillReferenced"/> is what makes content-addressing safe to delete from: two documents
    /// with identical bytes share one file, so removing one row must not pull the file out from under the other.
    /// The caller counts the rows, because only it can see the table.
    /// </remarks>
    public bool Delete(string relativePath, bool stillReferenced)
    {
        if (stillReferenced) return false;

        var full = Resolve(relativePath);
        if (full is null || !File.Exists(full)) return false;

        File.Delete(full);
        return true;
    }

    /// <summary>
    /// Resolves a stored relative path against the root, refusing anything that escapes it.
    /// </summary>
    /// <remarks>
    /// The paths this resolves are ones the store itself generated, so traversal should be impossible — but a
    /// path that reaches the filesystem is worth checking against the row it came from being wrong, and the
    /// check costs nothing. Belt and braces on the one code path that turns database content into a file read.
    /// </remarks>
    private string? Resolve(string relativePath)
    {
        var root = Path.GetFullPath(options.RootPath);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));

        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }
}
