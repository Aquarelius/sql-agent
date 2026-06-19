namespace SqlAgent.Core;

/// <summary>Database engine a connection targets. Drives provider selection (see CD-57).</summary>
public enum DatabaseProviderType
{
    SqlServer = 1,
    Postgres = 2,
}

/// <summary>Outcome of a connection test. Never throws back to the caller for a reachable-but-rejecting server.</summary>
public record ConnectionTestResult(bool Success, string? Error = null, string? ServerVersion = null, long ElapsedMs = 0)
{
    public static ConnectionTestResult Ok(string? serverVersion, long elapsedMs) => new(true, null, serverVersion, elapsedMs);
    public static ConnectionTestResult Fail(string error, long elapsedMs) => new(false, error, null, elapsedMs);
}

/// <summary>
/// Per-dialect database driver. v1 covers connection testing only; schema extraction, SQL
/// parsing/normalization, and execution land in later CD-50 tasks (T4–T6) on this same interface.
/// </summary>
public interface IDatabaseProvider
{
    DatabaseProviderType ProviderType { get; }

    /// <summary>Opens and closes a connection to verify the (draft or saved) connection string works.</summary>
    Task<ConnectionTestResult> TestConnectionAsync(string connectionString, CancellationToken ct = default);
}

/// <summary>Selects the <see cref="IDatabaseProvider"/> for a stored <see cref="DatabaseProviderType"/>.</summary>
public interface IDatabaseProviderRegistry
{
    IDatabaseProvider Get(DatabaseProviderType type);
}

/// <summary>Maps each registered provider by the type it reports. Built from the available providers.</summary>
public class DatabaseProviderRegistry : IDatabaseProviderRegistry
{
    private readonly IReadOnlyDictionary<DatabaseProviderType, IDatabaseProvider> _providers;

    public DatabaseProviderRegistry(IEnumerable<IDatabaseProvider> providers)
        => _providers = providers.ToDictionary(p => p.ProviderType);

    public IDatabaseProvider Get(DatabaseProviderType type)
        => _providers.TryGetValue(type, out var p)
            ? p
            : throw new NotSupportedException($"No database provider registered for {type}.");
}
