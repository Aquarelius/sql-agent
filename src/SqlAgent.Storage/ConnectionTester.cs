using SqlAgent.Core;

namespace SqlAgent.Storage;

/// <summary>
/// Tests database connections by selecting the provider from the stored/declared
/// <see cref="DatabaseProviderType"/>. Draft = caller-supplied string; saved = resolved from the secret store.
/// </summary>
public class ConnectionTester(DatabaseConnectionService connections, IDatabaseProviderRegistry providers)
{
    /// <summary>Tests an unsaved connection string against the given provider type.</summary>
    public Task<ConnectionTestResult> TestDraftAsync(
        DatabaseProviderType providerType, string connectionString, CancellationToken ct = default)
        => providers.Get(providerType).TestConnectionAsync(connectionString, ct);

    /// <summary>Tests a saved connection by id: resolves its secret and uses its stored provider type.</summary>
    public async Task<ConnectionTestResult?> TestSavedAsync(Guid id, CancellationToken ct = default)
    {
        var info = await connections.GetAsync(id, ct);
        if (info is null) return null;
        var connectionString = await connections.ResolveConnectionStringAsync(id, ct);
        if (connectionString is null) return null;
        return await providers.Get(info.ProviderType).TestConnectionAsync(connectionString, ct);
    }
}
