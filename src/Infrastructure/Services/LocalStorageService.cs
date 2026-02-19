using saas.Shared;

namespace saas.Infrastructure.Services;

/// <summary>
/// Local filesystem storage for development. Stores files under db/uploads/.
/// </summary>
public class LocalStorageService : IStorageService
{
    private readonly string _basePath;
    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(IWebHostEnvironment env, ILogger<LocalStorageService> logger)
    {
        _basePath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "db", "uploads"));
        Directory.CreateDirectory(_basePath);
        _logger = logger;
    }

    /// <summary>
    /// Resolves a relative path to a full path within the base directory.
    /// Throws if the resolved path escapes the base directory (path traversal protection).
    /// </summary>
    private string SafePath(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, path));
        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Invalid storage path: path escapes base directory");
        return fullPath;
    }

    public async Task<string> UploadAsync(Stream stream, string path, string contentType, CancellationToken ct = default)
    {
        var fullPath = SafePath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var fs = File.Create(fullPath);
        await stream.CopyToAsync(fs, ct);

        _logger.LogInformation("Stored file locally: {Path} ({ContentType})", path, contentType);
        return path;
    }

    public Task<Stream?> DownloadAsync(string path, CancellationToken ct = default)
    {
        var fullPath = SafePath(path);
        if (!File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
    {
        var fullPath = SafePath(path);
        if (!File.Exists(fullPath))
            return Task.FromResult(false);

        File.Delete(fullPath);
        _logger.LogInformation("Deleted local file: {Path}", path);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        var fullPath = SafePath(path);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task<string?> GetUrlAsync(string path, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        // Local storage doesn't support URLs
        return Task.FromResult<string?>(null);
    }
}
