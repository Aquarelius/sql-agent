using System.Diagnostics;
using Npgsql;
using SqlAgent.Core;

namespace SqlAgent.Providers.Postgres;

/// <summary>PostgreSQL provider (Npgsql). v1: connection testing only (CD-57).</summary>
public class PostgresProvider : IDatabaseProvider
{
    public DatabaseProviderType ProviderType => DatabaseProviderType.Postgres;

    public async Task<ConnectionTestResult> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            return ConnectionTestResult.Ok(conn.PostgreSqlVersion.ToString(), sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ConnectionTestResult.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }
}
