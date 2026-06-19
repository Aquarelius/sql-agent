using System.Diagnostics;
using Microsoft.Data.SqlClient;
using SqlAgent.Core;

namespace SqlAgent.Providers.SqlServer;

/// <summary>MS SQL Server provider. v1: connection testing only (CD-57).</summary>
public class SqlServerProvider : IDatabaseProvider
{
    public DatabaseProviderType ProviderType => DatabaseProviderType.SqlServer;

    public async Task<ConnectionTestResult> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            return ConnectionTestResult.Ok(conn.ServerVersion, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ConnectionTestResult.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }
}
