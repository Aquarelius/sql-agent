using Microsoft.EntityFrameworkCore;
using SqlAgent.Core;

namespace SqlAgent.Storage;

/// <summary>
/// Produces the policy-filtered schema description handed to the LLM: extracts the live schema via the
/// connection's provider, then omits every table a <see cref="TablePolicy"/> marks invisible (CD-50 T4).
/// Tables with no policy default to visible.
/// </summary>
public class SchemaService(
    DatabaseConnectionService connections, IDatabaseProviderRegistry providers, SqlAgentDbContext db)
{
    /// <summary>Returns the visible schema for a saved connection, or null if the connection or its secret is missing.</summary>
    public async Task<DatabaseSchema?> GetVisibleSchemaAsync(Guid connectionId, CancellationToken ct = default)
    {
        var info = await connections.GetAsync(connectionId, ct);
        if (info is null) return null;
        var connectionString = await connections.ResolveConnectionStringAsync(connectionId, ct);
        if (connectionString is null) return null;

        var schema = await providers.Get(info.ProviderType).GetSchemaAsync(connectionString, ct);

        var hidden = (await db.TablePolicies
                .Where(p => p.DatabaseConnectionId == connectionId && !p.IsVisible)
                .Select(p => new { p.SchemaName, p.TableName })
                .ToListAsync(ct))
            .Select(p => (p.SchemaName, p.TableName))
            .ToHashSet();

        return SchemaModel.Filter(schema, (s, t) => !hidden.Contains((s, t)));
    }
}
