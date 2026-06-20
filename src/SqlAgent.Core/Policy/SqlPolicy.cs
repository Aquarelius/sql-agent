using System.Collections;
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
        var collector = new RelationCollector();
        ((IElement)statement).Visit(collector);

        // The AST visitor walks query bodies (FROM/JOIN/CTE/subquery) but skips some DML targets, so we
        // add them explicitly. Only Write statements need this — Other is denied before tables are read.
        switch (statement)
        {
            case Statement.Insert ins:
                collector.Add(ins.InsertOperation.Name); // INSERT INTO <target> is not visited.
                break;
            case Statement.Delete del:
                // Visitor skips DELETE entirely; visiting its parts recovers target, USING, and WHERE subqueries.
                (del.DeleteOperation.From as IElement)?.Visit(collector);
                (del.DeleteOperation.Using as IElement)?.Visit(collector);
                (del.DeleteOperation.Selection as IElement)?.Visit(collector);
                break;
        }

        // A CTE name is a query-local alias, not a database table. `FROM <cte>` resolves to the CTE even
        // when a real table shares the name, so drop unqualified references that match a defined CTE.
        // Schema-qualified names (private.t) are never CTEs and always stay; the real base tables inside
        // CTE bodies are collected separately, so a hidden table can't hide behind a CTE.
        var cteNames = CollectCteNames(statement);
        var tables = collector.References
            .Where(t => !(t.Schema is null && cteNames.Contains(t.Name)))
            .ToList();

        var kind = statement switch
        {
            Statement.Select => SqlStatementKind.Read,
            Statement.Insert or Statement.Update or Statement.Delete => SqlStatementKind.Write,
            _ => SqlStatementKind.Other,
        };

        return new ParsedStatement(kind, statement.GetType().Name, tables);
    }

    /// <summary>
    /// Gathers every CTE name defined anywhere in the statement (including nested subqueries). The AST
    /// visitor never surfaces the <c>WITH</c> clause, so this walks the typed node graph generically and
    /// records each <c>WITH</c>'s CTE aliases. Comparison is case-insensitive to match SQL identifier rules.
    /// </summary>
    private static HashSet<string> CollectCteNames(Statement statement)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(statement, names, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return names;

        static void Walk(object? node, HashSet<string> names, HashSet<object> seen)
        {
            if (node is null or string) return;
            if (node is IEnumerable sequence)
            {
                foreach (var item in sequence) Walk(item, names, seen);
                return;
            }
            if (node.GetType().Namespace?.StartsWith("SqlParser.Ast", StringComparison.Ordinal) != true) return;
            if (!seen.Add(node)) return;

            if (node is With with)
                foreach (var cte in with.CteTables) names.Add(cte.Alias.Name.Value);

            foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                object? value;
                try { value = prop.GetValue(node); }
                catch { continue; }
                Walk(value, names, seen);
            }
        }
    }

    /// <summary>Collects every table reference the visitor reaches, de-duplicated on (schema, name).</summary>
    private sealed class RelationCollector : Visitor
    {
        private readonly List<SqlTableReference> _refs = [];
        private readonly HashSet<(string?, string)> _seen = [];

        public IReadOnlyList<SqlTableReference> References => _refs;

        public override ControlFlow PreVisitRelation(ObjectName name)
        {
            Add(name);
            return ControlFlow.Continue;
        }

        public void Add(ObjectName name)
        {
            // Identifier values are already unquoted ([dbo].[T] -> dbo, T). Last part is the table,
            // the part before it (if any) is the schema; deeper qualifiers (db.schema.table) are ignored.
            var parts = name.Values.Select(i => i.Value).ToList();
            if (parts.Count == 0) return;
            var table = parts[^1];
            var schema = parts.Count >= 2 ? parts[^2] : null;
            if (_seen.Add((schema, table)))
                _refs.Add(new SqlTableReference(schema, table));
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
    /// (the model defaults to visible). CTE-local names also reach this predicate and are expected to
    /// be treated as visible — the real tables behind a CTE are checked separately, so a hidden table
    /// cannot be masked by wrapping it in a CTE or alias.
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
