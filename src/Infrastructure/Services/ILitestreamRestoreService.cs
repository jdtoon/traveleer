namespace saas.Infrastructure.Services;

public interface ILitestreamRestoreService
{
    Task RestoreIfNeededAsync(CancellationToken ct = default);
}
