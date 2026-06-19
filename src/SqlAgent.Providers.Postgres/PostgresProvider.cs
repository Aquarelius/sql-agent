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

    public async Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // System schemas are noise to the LLM; only describe user tables.
        const string userSchemas = "c.table_schema NOT IN ('pg_catalog', 'information_schema')";

        var columns = await Query(conn, ct,
            $"""
            SELECT c.table_schema, c.table_name, c.column_name, c.data_type, c.is_nullable
            FROM information_schema.columns c
            JOIN information_schema.tables t
              ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            WHERE t.table_type = 'BASE TABLE' AND {userSchemas}
            ORDER BY c.table_schema, c.table_name, c.ordinal_position
            """,
            r => (r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                  string.Equals(r.GetString(4), "YES", StringComparison.OrdinalIgnoreCase)));

        var pks = await Query(conn, ct,
            """
            SELECT tc.table_schema, tc.table_name, kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON kcu.constraint_name = tc.constraint_name AND kcu.constraint_schema = tc.constraint_schema
            WHERE tc.constraint_type = 'PRIMARY KEY'
              AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY tc.table_schema, tc.table_name, kcu.ordinal_position
            """,
            r => (r.GetString(0), r.GetString(1), r.GetString(2)));

        var fks = await Query(conn, ct,
            """
            SELECT tc.table_schema, tc.table_name, kcu.column_name,
                   ccu.table_schema, ccu.table_name, ccu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON kcu.constraint_name = tc.constraint_name AND kcu.constraint_schema = tc.constraint_schema
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name AND ccu.constraint_schema = tc.constraint_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')
            """,
            r => (r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));

        return SchemaModel.Build(columns, pks, fks);
    }

    private static async Task<List<T>> Query<T>(
        NpgsqlConnection conn, CancellationToken ct, string sql, Func<NpgsqlDataReader, T> map)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
            rows.Add(map(reader));
        return rows;
    }
}
