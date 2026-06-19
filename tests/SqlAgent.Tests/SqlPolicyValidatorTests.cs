using SqlAgent.Core;
using SqlAgent.Core.Policy;

namespace SqlAgent.Tests;

public class SqlPolicyValidatorTests
{
    // Default: every table visible. Individual tests override with a hidden set.
    private static Func<SqlTableReference, bool> Visible(params string[] hidden)
    {
        var hiddenSet = hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return t => !hiddenSet.Contains(t.Name) && !hiddenSet.Contains(t.ToString());
    }

    private static PolicyDecision Validate(
        string sql,
        bool isReadOnly = false,
        DatabaseProviderType provider = DatabaseProviderType.Postgres,
        Func<SqlTableReference, bool>? isVisible = null)
        => SqlPolicyValidator.Validate(sql, provider, isReadOnly, isVisible ?? Visible());

    // --- Read-only enforcement -------------------------------------------------

    [Theory]
    [InlineData("UPDATE orders SET total = 0")]
    [InlineData("INSERT INTO orders (id) VALUES (1)")]
    [InlineData("DELETE FROM orders WHERE id = 1")]
    [InlineData("DROP TABLE orders")]
    [InlineData("TRUNCATE TABLE orders")]
    [InlineData("CREATE TABLE t (id int)")]
    public void ReadOnly_denies_mutating_and_ddl(string sql)
    {
        var d = Validate(sql, isReadOnly: true);
        Assert.False(d.Allowed);
        Assert.Contains(d.DenyCode, new[] { "policy_denied_readonly", "policy_denied_unsupported" });
    }

    [Fact]
    public void ReadOnly_allows_select()
    {
        var d = Validate("SELECT id FROM orders", isReadOnly: true);
        Assert.True(d.Allowed);
    }

    [Fact]
    public void Writable_allows_dml()
    {
        var d = Validate("UPDATE orders SET total = 0", isReadOnly: false);
        Assert.True(d.Allowed);
    }

    // --- Unsupported statements (fail closed, even on writable) -----------------

    [Theory]
    [InlineData("DROP TABLE orders")]
    [InlineData("ALTER TABLE orders ADD c int")]
    [InlineData("TRUNCATE TABLE orders")]
    public void Unsupported_statements_are_denied_even_when_writable(string sql)
    {
        var d = Validate(sql, isReadOnly: false);
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_unsupported", d.DenyCode);
    }

    // --- Multi-statement batches -----------------------------------------------

    [Fact]
    public void Multi_statement_batch_is_denied()
    {
        var d = Validate("SELECT * FROM orders; SELECT * FROM customers");
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_multi_statement", d.DenyCode);
    }

    [Fact]
    public void Stacked_select_then_delete_is_denied_as_batch()
    {
        // Classic injection shape: a read followed by a hidden write.
        var d = Validate("SELECT 1; DELETE FROM orders", isReadOnly: true);
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_multi_statement", d.DenyCode);
    }

    // --- Table visibility: aliases, joins, CTEs, subqueries --------------------

    [Fact]
    public void Hidden_table_via_alias_is_denied()
    {
        var d = Validate("SELECT s.x FROM secrets s", isVisible: Visible("secrets"));
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_hidden_table", d.DenyCode);
    }

    [Fact]
    public void Hidden_table_in_join_is_denied()
    {
        var d = Validate(
            "SELECT o.id FROM orders o JOIN secrets s ON s.id = o.sid",
            isVisible: Visible("secrets"));
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_hidden_table", d.DenyCode);
    }

    [Fact]
    public void Hidden_table_behind_cte_is_denied()
    {
        // The CTE name `t` is visible-by-default, but the real `secrets` table inside it must be caught.
        var d = Validate(
            "WITH t AS (SELECT * FROM secrets) SELECT * FROM t",
            isVisible: Visible("secrets"));
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_hidden_table", d.DenyCode);
    }

    [Fact]
    public void Hidden_table_in_subquery_is_denied()
    {
        var d = Validate(
            "SELECT * FROM orders WHERE sid IN (SELECT id FROM secrets)",
            isVisible: Visible("secrets"));
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_hidden_table", d.DenyCode);
    }

    [Fact]
    public void Hidden_table_as_insert_target_is_denied()
    {
        // Visitor skips the INSERT target — proves the explicit target extraction works.
        var d = Validate("INSERT INTO secrets (id) VALUES (1)", isVisible: Visible("secrets"));
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_hidden_table", d.DenyCode);
    }

    [Fact]
    public void Hidden_table_as_delete_target_is_denied()
    {
        // Visitor skips DELETE entirely — proves the From/Selection re-visit works.
        var d = Validate("DELETE FROM secrets WHERE id = 1", isVisible: Visible("secrets"));
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_hidden_table", d.DenyCode);
    }

    [Fact]
    public void Schema_qualified_hidden_table_is_denied()
    {
        var d = Validate("SELECT * FROM private.secrets", isVisible: Visible("private.secrets"));
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_hidden_table", d.DenyCode);
    }

    [Fact]
    public void Visible_query_with_comments_and_joins_is_allowed()
    {
        var sql = "-- monthly report\nSELECT o.id, c.name FROM orders o JOIN customers c ON c.id = o.cid";
        var d = Validate(sql);
        Assert.True(d.Allowed);
        Assert.Contains(d.ReferencedTables, t => t.Name == "orders");
        Assert.Contains(d.ReferencedTables, t => t.Name == "customers");
    }

    // --- Dialect awareness -----------------------------------------------------

    [Fact]
    public void SqlServer_dialect_parses_bracketed_identifiers_and_top()
    {
        var d = Validate(
            "SELECT TOP 10 * FROM [dbo].[secrets]",
            provider: DatabaseProviderType.SqlServer,
            isVisible: Visible("secrets"));
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_hidden_table", d.DenyCode);
    }

    // --- Malformed / empty input ----------------------------------------------

    [Fact]
    public void Unparseable_sql_is_denied()
    {
        var d = Validate("SELCT FROM WHERE");
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_parse_error", d.DenyCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- just a comment")]
    public void Empty_input_is_denied(string sql)
    {
        var d = Validate(sql);
        Assert.False(d.Allowed);
        Assert.Equal("policy_denied_empty", d.DenyCode);
    }
}
