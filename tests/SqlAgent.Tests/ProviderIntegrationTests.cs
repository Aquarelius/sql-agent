using Microsoft.Data.SqlClient;
using SqlAgent.Core;
using SqlAgent.Providers.Postgres;
using SqlAgent.Providers.SqlServer;

namespace SqlAgent.Tests;

public class ProviderIntegrationTests
{
    private const string PostgresConnectionStringEnv = "SQLAGENT_TEST_POSTGRES";
    private const string SqlServerConnectionStringEnv = "SQLAGENT_TEST_SQLSERVER";

    [Fact]
    public async Task Postgres_fixture_supports_connection_test_and_query_execution()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var provider = new PostgresProvider();
        var connection = await provider.TestConnectionAsync(connectionString);
        Assert.True(connection.Success, connection.Error);

        var result = await provider.ExecuteQueryAsync(
            connectionString,
            "SELECT 1::integer AS value",
            QueryExecutionOptions.Default);

        Assert.Equal(["value"], result.Columns);
        var row = Assert.Single(result.Rows);
        Assert.Equal(1, Convert.ToInt32(row[0]));
    }

    [Fact]
    public async Task SqlServer_fixture_supports_connection_test_and_query_execution()
    {
        var connectionString = Environment.GetEnvironmentVariable(SqlServerConnectionStringEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var provider = new SqlServerProvider();
        var connection = await provider.TestConnectionAsync(connectionString);
        Assert.True(connection.Success, connection.Error);

        var result = await provider.ExecuteQueryAsync(
            connectionString,
            "SELECT CAST(1 AS int) AS value",
            QueryExecutionOptions.Default);

        Assert.Equal(["value"], result.Columns);
        var row = Assert.Single(result.Rows);
        Assert.Equal(1, Convert.ToInt32(row[0]));
    }

    [Fact]
    public async Task SqlServer_schema_extraction_covers_types_composite_keys_and_fk_order()
    {
        var connectionString = Environment.GetEnvironmentVariable(SqlServerConnectionStringEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        // Composite PK on the parent; composite FK (OrderId, Sku) on the child, plus a nullable
        // nvarchar and a decimal so length/precision/scale all have something to assert.
        const string schema = "sqlagent_ct";
        await ExecAsync(connectionString,
            $"""
            IF OBJECT_ID('{schema}.OrderLine') IS NOT NULL DROP TABLE {schema}.OrderLine;
            IF OBJECT_ID('{schema}.OrderItem') IS NOT NULL DROP TABLE {schema}.OrderItem;
            IF SCHEMA_ID('{schema}') IS NULL EXEC('CREATE SCHEMA {schema}');
            CREATE TABLE {schema}.OrderItem (
                OrderId int NOT NULL,
                Sku varchar(20) NOT NULL,
                CONSTRAINT PK_OrderItem PRIMARY KEY (OrderId, Sku));
            CREATE TABLE {schema}.OrderLine (
                LineId int NOT NULL CONSTRAINT PK_OrderLine PRIMARY KEY,
                OrderId int NOT NULL,
                Sku varchar(20) NOT NULL,
                Note nvarchar(100) NULL,
                Price decimal(18,2) NOT NULL,
                CONSTRAINT FK_OrderLine_OrderItem FOREIGN KEY (OrderId, Sku)
                    REFERENCES {schema}.OrderItem (OrderId, Sku));
            """);
        try
        {
            var db = await new SqlServerProvider().GetSchemaAsync(connectionString);

            var item = db.Tables.Single(t => t.Schema == schema && t.Name == "OrderItem");
            Assert.Equal(["OrderId", "Sku"], item.PrimaryKey); // composite PK, in declared order

            var line = db.Tables.Single(t => t.Schema == schema && t.Name == "OrderLine");

            // Composite FK comes back as ordered column pairs, each local column lined up with its target.
            Assert.Equal(
                [("OrderId", "OrderItem", "OrderId"), ("Sku", "OrderItem", "Sku")],
                line.ForeignKeys.Select(f => (f.Column, f.ReferencedTable, f.ReferencedColumn)));

            var note = line.Columns.Single(c => c.Name == "Note");
            Assert.True(note.IsNullable);
            Assert.Equal(100, note.MaxLength);

            var price = line.Columns.Single(c => c.Name == "Price");
            Assert.False(price.IsNullable);
            Assert.Equal(18, price.Precision);
            Assert.Equal(2, price.Scale);

            Assert.Equal(20, line.Columns.Single(c => c.Name == "Sku").MaxLength);
        }
        finally
        {
            await ExecAsync(connectionString,
                $"""
                IF OBJECT_ID('{schema}.OrderLine') IS NOT NULL DROP TABLE {schema}.OrderLine;
                IF OBJECT_ID('{schema}.OrderItem') IS NOT NULL DROP TABLE {schema}.OrderItem;
                IF SCHEMA_ID('{schema}') IS NOT NULL EXEC('DROP SCHEMA {schema}');
                """);
        }
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
