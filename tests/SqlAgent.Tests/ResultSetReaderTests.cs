using Microsoft.Data.Sqlite;
using SqlAgent.Core;

namespace SqlAgent.Tests;

/// <summary>
/// Drives <see cref="ResultSetReader"/> with a real ADO.NET reader (in-memory SQLite) so the row-cap /
/// truncation contract is exercised end to end without a server.
/// </summary>
public class ResultSetReaderTests
{
    private static SqliteConnection SeededDb(int rows)
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var create = conn.CreateCommand();
        create.CommandText = "CREATE TABLE nums (id INTEGER, label TEXT);";
        create.ExecuteNonQuery();
        for (var i = 1; i <= rows; i++)
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = $"INSERT INTO nums (id, label) VALUES ({i}, 'row{i}');";
            insert.ExecuteNonQuery();
        }
        return conn;
    }

    private static async Task<QueryResultSet> Read(SqliteConnection conn, int maxRows)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, label FROM nums ORDER BY id";
        await using var reader = await cmd.ExecuteReaderAsync();
        return await ResultSetReader.ReadAsync(reader, maxRows, CancellationToken.None);
    }

    [Fact]
    public async Task Reads_columns_and_rows_under_the_cap()
    {
        using var conn = SeededDb(3);
        var set = await Read(conn, maxRows: 10);

        Assert.Equal(["id", "label"], set.Columns);
        Assert.False(set.Truncated);
        Assert.Equal(3, set.Rows.Count);
        Assert.Equal(1L, set.Rows[0][0]);
        Assert.Equal("row1", set.Rows[0][1]);
    }

    [Fact]
    public async Task Caps_rows_and_flags_truncation_when_more_exist()
    {
        using var conn = SeededDb(5);
        var set = await Read(conn, maxRows: 3);

        Assert.True(set.Truncated);
        Assert.Equal(3, set.Rows.Count);
    }

    [Fact]
    public async Task Exactly_maxrows_is_not_truncated()
    {
        using var conn = SeededDb(3);
        var set = await Read(conn, maxRows: 3);

        Assert.False(set.Truncated);
        Assert.Equal(3, set.Rows.Count);
    }

    [Fact]
    public async Task Null_values_come_back_as_null()
    {
        using var conn = SeededDb(0);
        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT INTO nums (id, label) VALUES (1, NULL);";
            insert.ExecuteNonQuery();
        }

        var set = await Read(conn, maxRows: 10);

        Assert.Single(set.Rows);
        Assert.Null(set.Rows[0][1]);
    }
}
