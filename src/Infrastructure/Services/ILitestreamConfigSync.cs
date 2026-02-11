namespace saas.Infrastructure.Services;

/// <summary>
/// Allows immediate triggering of Litestream config sync (e.g. after tenant creation).
/// </summary>
public interface ILitestreamConfigSync
{
    Task SyncConfigAsync();
}
