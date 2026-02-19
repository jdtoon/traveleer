using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace saas.Data.Tenant;

/// <summary>
/// Sets PRAGMA journal_mode=WAL and busy_timeout on every new SQLite connection open.
/// WAL allows concurrent readers; busy_timeout makes writers wait (up to 5s) instead
/// of failing immediately with SQLITE_BUSY when the database is locked.
/// </summary>
public class WalModeInterceptor : DbConnectionInterceptor
{
    private const string Pragmas = """
        PRAGMA journal_mode=WAL;
        PRAGMA busy_timeout = 5000;
        """;

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }
}
