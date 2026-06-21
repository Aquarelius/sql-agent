namespace SqlAgent.Core;

/// <summary>
/// Per-provider dialect guidance prepended to the schema text in the NL-to-SQL prompt (CD-71, epic CD-53
/// risk: T-SQL and PostgreSQL dialects differ enough that LLM-generated SQL may not be portable). Plain
/// text, not executed — it only steers generation toward the syntax the target engine actually accepts.
/// Centralized here so every prompt surface gets identical hints.
/// </summary>
public static class DialectHints
{
    public static string For(DatabaseProviderType provider) => provider switch
    {
        DatabaseProviderType.SqlServer =>
            "Target dialect: Microsoft SQL Server (T-SQL).\n" +
            "- Limit rows with TOP n (e.g. SELECT TOP 10 ...); LIMIT is not supported.\n" +
            "- Use GETDATE() for the current date/time.\n" +
            "- Quote identifiers with square brackets [like this]; identifiers are case-insensitive.",

        DatabaseProviderType.Postgres =>
            "Target dialect: PostgreSQL.\n" +
            "- Limit rows with LIMIT n (e.g. SELECT ... LIMIT 10); TOP is not supported.\n" +
            "- Use NOW() for the current date/time.\n" +
            "- Use RETURNING to read back rows affected by INSERT/UPDATE/DELETE.\n" +
            "- Quote identifiers with double quotes \"like this\"; unquoted identifiers fold to lower case.",

        _ => throw new NotSupportedException($"No dialect hints defined for provider {provider}."),
    };
}
