namespace saas.Shared;

/// <summary>
/// Generic blob storage interface. Backed by Cloudflare R2 in production
/// and local filesystem in development.
/// </summary>
public interface IStorageService
{
    /// <summary>Upload a file and return its storage path.</summary>
    Task<string> UploadAsync(Stream stream, string path, string contentType, CancellationToken ct = default);

    /// <summary>Download a file by storage path.</summary>
    Task<Stream?> DownloadAsync(string path, CancellationToken ct = default);

    /// <summary>Delete a file by storage path.</summary>
    Task<bool> DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>Check if a file exists at the given path.</summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>Get a pre-signed or public URL for a file. Returns null if not supported.</summary>
    Task<string?> GetUrlAsync(string path, TimeSpan? expiry = null, CancellationToken ct = default);
}
