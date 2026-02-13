using System.IO.Compression;
using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class KeyRingBackupService : BackgroundService
{
    private readonly IStorageService _storageService;
    private readonly BackupOptions _options;
    private readonly ILogger<KeyRingBackupService> _logger;
    private readonly string _keysDirectory;
    private readonly TimeSpan _interval;

    public KeyRingBackupService(
        IWebHostEnvironment environment,
        IStorageService storageService,
        IOptions<BackupOptions> options,
        ILogger<KeyRingBackupService> logger)
    {
        _storageService = storageService;
        _options = options.Value;
        _logger = logger;
        _keysDirectory = Path.Combine(environment.ContentRootPath, "db", "keys");
        _interval = DurationParser.ParseOrDefault(_options.KeyBackupInterval, TimeSpan.FromHours(1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.KeyBackupEnabled)
        {
            _logger.LogInformation("DataProtection key backup service is disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BackupKeysAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backup DataProtection keys");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task BackupKeysAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_keysDirectory))
        {
            _logger.LogDebug("Key directory does not exist yet: {Path}", _keysDirectory);
            return;
        }

        var files = Directory.GetFiles(_keysDirectory, "*", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            _logger.LogDebug("No DataProtection key files to back up");
            return;
        }

        await using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream, ct);
            }
        }

        zipStream.Position = 0;
        await _storageService.UploadAsync(zipStream, _options.KeyBackupPath, "application/zip", ct);

        var markerDir = Path.GetDirectoryName(_options.KeyBackupMarkerPath);
        if (!string.IsNullOrWhiteSpace(markerDir))
            Directory.CreateDirectory(markerDir);

        await File.WriteAllTextAsync(_options.KeyBackupMarkerPath, DateTime.UtcNow.ToString("O"), ct);

        _logger.LogInformation("Backed up {Count} DataProtection key files to {Path}", files.Length, _options.KeyBackupPath);
    }
}
