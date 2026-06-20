using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

namespace SqlAgent.Core.Policy;

/// <summary>
/// How a parsed statement relates to the read-only policy. The agent only ever runs read queries
/// and (on writable connections) basic DML; anything else is <see cref="Other"/> and denied.
/// </summary>
public enum SqlStatementKind
{
    /// <summary>SELECT — allowed on read-only and writable connections (visibility permitting).</summary>
    Read,

    /// <summary>INSERT / UPDATE / DELETE — allowed only when the connection is not read-only.</summary>
    Write,

    /// <summary>DDL, EXEC, TRUNCATE, etc. — never supported by the agent in v1 (fail closed).</summary>
    Other,
}

/// <summary>A table named by a statement. <see cref="Schema"/> is null when the SQL left it unqualified.</summary>
public record SqlTableReference(string? Schema, string Name)
{
    public override string ToString() => Schema is null ? Name : $"{Schema}.{Name}";
}

/// <summary>One parsed statement: its kind, the parser's concrete type name, and every table it touches.</summary>
public record ParsedStatement(SqlStatementKind Kind, string StatementType, IReadOnlyList<SqlTableReference> Tables);

/// <summary>
/// Dialect-aware SQL parsing (ADR-0002, CD-50 T5). Turns raw SQL into <see cref="ParsedStatement"/>s
/// exposing statement type and referenced objects, so policy checks never touch the raw text with regex.
/// </summary>
public static class SqlAnalyzer
{
    /// <summary>
    /// Parses <paramref name="sql"/> with the dialect for <paramref name="provider"/>. A batch may yield
    /// several statements (the caller rejects multi-statement input). Throws <see cref="ParserException"/>
    /// on invalid SQL; callers treat that as a fail-closed denial.
    /// </summary>
    public static IReadOnlyList<ParsedStatement> Analyze(string sql, DatabaseProviderType provider)
    {
        var statements = new Parser().ParseSql(sql, DialectFor(provider));
        return statements.Select(Describe).ToList();
    }

    private static Dialect DialectFor(DatabaseProviderType provider) => provider switch
    {
        DatabaseProviderType.SqlServer => new MsSqlDialect(),
        DatabaseProviderType.Postgres => new PostgreSqlDialect(),
        _ => throw new NotSupportedException($"No SQL dialect mapped for provider {provider}."),
    };

    private static ParsedStatement Describe(Statement statement)
    {
        var collector = new TableCollector();
        collector.Walk(statement, ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase));

        var kind = statement switch
        {
            Statement.Select => SqlStatementKind.Read,
            Statement.Insert or Statement.Update or Statement.Delete => SqlStatementKind.Write,
            _ => SqlStatementKind.Other,
        };

        return new ParsedStatement(kind, statement.GetType().Name, collector.References);
    }

    /// <summary>
    /// Walks the parsed AST collecting real table references, scope-aware about CTEs. The SqlParserCS
    /// visitor is flat (no <c>Query</c> hook) so it cannot track which CTE names are in scope where; this
    /// generic walk does. A <c>WITH</c> clause adds its CTE names to the scope of the query it heads (and
    /// every descendant), so <c>FROM &lt;cte&gt;</c> resolves to the alias and is dropped — but a real
    /// table of the same name in an outer or sibling scope keeps being checked. Schema-qualified names are
    /// never CTEs and always stay. Catches tables behind aliases, joins, CTE bodies, subqueries, and DML
    /// targets in one pass.
    /// </summary>
    private sealed class TableCollector
    {
        private readonly List<SqlTableReference> _refs = [];
        private readonly HashSet<(string?, string)> _seen = [];
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

        public IReadOnlyList<SqlTableReference> References => _refs;

        public void Walk(object? node, ImmutableHashSet<string> scope)
        {
            if (node is null or string) return;
            if (node is IEnumerable sequence)
            {
                foreach (var item in sequence) Walk(item, scope);
                return;
            }
            if (node.GetType().Namespace?.StartsWith("SqlParser.Ast", StringComparison.Ordinal) != true) return;
            if (!_visited.Add(node)) return;

            // A WITH clause brings its CTE names into scope for this query and everything nested below it.
            if (node is Query { With: { } with })
                scope = scope.Union(with.CteTables.Select(c => c.Alias.Name.Value));

            // Real table references: FROM/JOIN targets, plus UPDATE/DELETE targets (also TableFactor.Table).
            if (node is TableFactor.Table table)
                AddUnlessCte(table.Name, scope);
            // INSERT target is a bare ObjectName, not a TableFactor, so pick it up explicitly.
            else if (node is Statement.Insert insert)
                AddUnlessCte(insert.InsertOperation.Name, scope);

            foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                object? value;
                try { value = prop.GetValue(node); }
                catch { continue; }
                Walk(value, scope);
            }
        }

        private void AddUnlessCte(ObjectName name, ImmutableHashSet<string> scope)
        {
            // Identifier values are already unquoted ([dbo].[T] -> dbo, T). Last part is the table, the
            // part before it (if any) is the schema; deeper qualifiers (db.schema.table) are ignored.
            var parts = name.Values.Select(i => i.Value).ToList();
            if (parts.Count == 0) return;
            var tableName = parts[^1];
            var schema = parts.Count >= 2 ? parts[^2] : null;

            // Unqualified name matching an in-scope CTE is an alias, not a table — skip it.
            if (schema is null && scope.Contains(tableName)) return;

            if (_seen.Add((schema, tableName)))
                _refs.Add(new SqlTableReference(schema, tableName));
        }
    }
}

/// <summary>Allow/deny outcome with a stable code (for audit + client error mapping) and the tables seen.</summary>
public record PolicyDecision(
    bool Allowed,
    string? DenyCode,
    string? Reason,
    IReadOnlyList<SqlTableReference> ReferencedTables)
{
    public static PolicyDecision Allow(IReadOnlyList<SqlTableReference> tables) => new(true, null, null, tables);

    public static PolicyDecision Deny(string code, string reason, IReadOnlyList<SqlTableReference> tables)
        => new(false, code, reason, tables);
}

/// <summary>
/// Applies connection policy to parsed SQL (CD-50 T5): rejects multi-statement batches, unsupported
/// statements, mutating statements on read-only connections, and any reference to a hidden table —
/// all before execution. Decoupled from storage: visibility is supplied as a predicate.
/// </summary>
public static class SqlPolicyValidator
{
    /// <param name="isVisible">
    /// Returns false for a table the policy hides. Tables with no policy row should return true
    /// (the model defaults to visible). Only real tables reach this predicate: CTE aliases are resolved
    /// out scope-aware during parsing, while the real base tables inside a CTE body are still passed in,
    /// so a hidden table cannot be masked by wrapping it in a CTE or alias.
    /// </param>
    public static PolicyDecision Validate(
        string sql,
        DatabaseProviderType provider,
        bool isReadOnly,
        Func<SqlTableReference, bool> isVisible)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return PolicyDecision.Deny("policy_denied_empty", "No executable SQL statement was provided.", []);

        IReadOnlyList<ParsedStatement> statements;
        try
        {
            statements = SqlAnalyzer.Analyze(sql, provider);
        }
        catch (ParserException ex)
        {
            return PolicyDecision.Deny("policy_denied_parse_error", $"SQL could not be parsed: {ex.Message}", []);
        }

        if (statements.Count == 0)
            return PolicyDecision.Deny("policy_denied_empty", "No executable SQL statement was provided.", []);

        if (statements.Count > 1)
        {
            var all = statements.SelectMany(s => s.Tables).ToList();
            return PolicyDecision.Deny(
                "policy_denied_multi_statement",
                $"Multi-statement batches are not allowed ({statements.Count} statements found).",
                all);
        }

        var stmt = statements[0];

        if (stmt.Kind == SqlStatementKind.Other)
            return PolicyDecision.Deny(
                "policy_denied_unsupported",
                $"Statement type '{stmt.StatementType}' is not supported.",
                stmt.Tables);

        if (isReadOnly && stmt.Kind != SqlStatementKind.Read)
            return PolicyDecision.Deny(
                "policy_denied_readonly",
                $"Connection is read-only; '{stmt.StatementType}' would modify data.",
                stmt.Tables);

        var hidden = stmt.Tables.Where(t => !isVisible(t)).ToList();
        if (hidden.Count > 0)
            return PolicyDecision.Deny(
                "policy_denied_hidden_table",
                $"References table(s) not visible to this connection: {string.Join(", ", hidden)}.",
                stmt.Tables);

        return PolicyDecision.Allow(stmt.Tables);
    }
}
