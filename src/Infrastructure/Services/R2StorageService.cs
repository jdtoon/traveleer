using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Infrastructure.Services;

/// <summary>
/// Cloudflare R2 blob storage via S3-compatible API.
/// Uses the AWS SDK (already a project dependency for SES).
/// </summary>
public class R2StorageService : IStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly StorageOptions _options;
    private readonly ILogger<R2StorageService> _logger;

    public R2StorageService(IOptions<StorageOptions> options, ILogger<R2StorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.R2AccessKey) || string.IsNullOrWhiteSpace(_options.R2SecretKey))
            throw new InvalidOperationException(
                "Storage:Provider is R2 but Storage:R2AccessKey and Storage:R2SecretKey are not configured.");

        if (string.IsNullOrWhiteSpace(_options.R2Endpoint))
            throw new InvalidOperationException("Storage:R2Endpoint must be configured for R2 provider.");

        var credentials = new BasicAWSCredentials(_options.R2AccessKey, _options.R2SecretKey);
        var config = new AmazonS3Config
        {
            ServiceURL = _options.R2Endpoint,
            ForcePathStyle = true
        };

        _s3 = new AmazonS3Client(credentials, config);
    }

    public async Task<string> UploadAsync(Stream stream, string path, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.R2Bucket,
            Key = path,
            InputStream = stream,
            ContentType = contentType,
            DisablePayloadSigning = true
        };

        await _s3.PutObjectAsync(request, ct);
        _logger.LogInformation("Uploaded to R2: {Bucket}/{Path}", _options.R2Bucket, path);
        return path;
    }

    public async Task<Stream?> DownloadAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_options.R2Bucket, path, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string path, CancellationToken ct = default)
    {
        try
        {
            await _s3.DeleteObjectAsync(_options.R2Bucket, path, ct);
            _logger.LogInformation("Deleted from R2: {Bucket}/{Path}", _options.R2Bucket, path);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_options.R2Bucket, path, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public Task<string?> GetUrlAsync(string path, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        // If a public URL is configured, return a direct URL
        if (!string.IsNullOrWhiteSpace(_options.R2PublicUrl))
        {
            var publicUrl = $"{_options.R2PublicUrl.TrimEnd('/')}/{path}";
            return Task.FromResult<string?>(publicUrl);
        }

        // Otherwise generate a pre-signed URL
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.R2Bucket,
            Key = path,
            Expires = DateTime.UtcNow.Add(expiry ?? TimeSpan.FromHours(1))
        };

        var url = _s3.GetPreSignedURL(request);
        return Task.FromResult<string?>(url);
    }
}
