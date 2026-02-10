namespace saas.Shared;

/// <summary>
/// Configuration for blob storage. Bound to "Storage" section in appsettings.
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>"Local" for filesystem, "R2" for Cloudflare R2.</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>Local filesystem base path for uploads (relative to content root).</summary>
    public string LocalBasePath { get; set; } = "db/uploads";

    /// <summary>Cloudflare R2 bucket name.</summary>
    public string? R2Bucket { get; set; }

    /// <summary>Cloudflare R2 S3-compatible endpoint URL.</summary>
    public string? R2Endpoint { get; set; }

    /// <summary>R2 access key ID (S3-compatible).</summary>
    public string? R2AccessKey { get; set; }

    /// <summary>R2 secret access key (S3-compatible).</summary>
    public string? R2SecretKey { get; set; }

    /// <summary>Public URL prefix for R2 assets (e.g. custom domain or r2.dev URL).</summary>
    public string? R2PublicUrl { get; set; }
}
