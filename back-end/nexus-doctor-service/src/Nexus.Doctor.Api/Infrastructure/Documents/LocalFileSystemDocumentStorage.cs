using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Nexus.Doctors.Api.Configuration;

namespace Nexus.Doctors.Api.Infrastructure.Documents;

/// <summary>
/// Local-filesystem implementation of <see cref="IDocumentStorage"/>. Writes into
/// <c>Documents:RootPath</c> using the supplied storage key as the relative path.
/// SHA-256 is computed in a single pass while the file is being streamed to disk.
/// </summary>
public sealed class LocalFileSystemDocumentStorage : IDocumentStorage
{
    private readonly DocumentsOptions _options;
    private readonly ILogger<LocalFileSystemDocumentStorage> _logger;

    public LocalFileSystemDocumentStorage(IOptions<DocumentsOptions> options, ILogger<LocalFileSystemDocumentStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StoredDocument> StoreAsync(string storageKey, Stream source, CancellationToken ct)
    {
        var path = Resolve(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var sha = SHA256.Create();
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await using var crypto = new CryptoStream(output, sha, CryptoStreamMode.Write, leaveOpen: true);

        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            await crypto.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
        }

        await crypto.FlushAsync(ct);
        crypto.FlushFinalBlock();

        var hash = sha.Hash ?? Array.Empty<byte>();
        return new StoredDocument(Convert.ToHexString(hash).ToLowerInvariant(), total);
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        var path = Resolve(storageKey);
        Stream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        return Task.FromResult(s);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        var path = Resolve(storageKey);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete {Path}", path);
        }
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        var safe = storageKey.Replace('\\', '/').TrimStart('/');
        return Path.Combine(_options.RootPath, safe);
    }
}
