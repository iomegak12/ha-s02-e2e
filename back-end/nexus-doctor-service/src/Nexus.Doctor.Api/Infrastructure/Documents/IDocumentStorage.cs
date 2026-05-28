namespace Nexus.Doctors.Api.Infrastructure.Documents;

/// <summary>
/// Pluggable storage for doctor verification documents. Streams in (returning the
/// final byte count + SHA-256) and streams out. Provider-agnostic — Phase 7 ships
/// the <see cref="LocalFileSystemDocumentStorage"/> implementation.
/// </summary>
public interface IDocumentStorage
{
    /// <summary>
    /// Stream <paramref name="source"/> into storage at the given <paramref name="storageKey"/>.
    /// Returns the SHA-256 hex digest computed in the same pass and the byte length written.
    /// </summary>
    Task<StoredDocument> StoreAsync(string storageKey, Stream source, CancellationToken ct);

    /// <summary>Open the underlying file for read.</summary>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct);

    /// <summary>Best-effort delete; no-op if the file is missing.</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct);
}

/// <summary>Result returned by <see cref="IDocumentStorage.StoreAsync"/>.</summary>
public sealed record StoredDocument(string Sha256Hex, long SizeBytes);
