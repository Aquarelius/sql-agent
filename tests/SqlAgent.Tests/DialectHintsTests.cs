using SqlAgent.Core;

namespace SqlAgent.Tests;

public class DialectHintsTests
{
    [Theory]
    [InlineData("TOP")]
    [InlineData("GETDATE")]
    [InlineData("[like this]")] // bracket identifier quoting
    public void SqlServer_hints_cover_top_getdate_and_identifiers(string expected)
        => Assert.Contains(expected, DialectHints.For(DatabaseProviderType.SqlServer));

    [Theory]
    [InlineData("LIMIT")]
    [InlineData("NOW")]
    [InlineData("RETURNING")]
    [InlineData("double quotes")] // identifier quoting
    public void Postgres_hints_cover_limit_now_returning_and_identifiers(string expected)
        => Assert.Contains(expected, DialectHints.For(DatabaseProviderType.Postgres));

    [Fact]
    public void Each_provider_names_its_own_dialect_only()
    {
        Assert.Contains("SQL Server", DialectHints.For(DatabaseProviderType.SqlServer));
        Assert.DoesNotContain("LIMIT n", DialectHints.For(DatabaseProviderType.SqlServer));

        Assert.Contains("PostgreSQL", DialectHints.For(DatabaseProviderType.Postgres));
        Assert.DoesNotContain("TOP n", DialectHints.For(DatabaseProviderType.Postgres));
    }
}
