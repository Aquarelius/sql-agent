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
}
