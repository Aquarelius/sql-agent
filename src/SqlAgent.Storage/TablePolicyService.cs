using Microsoft.EntityFrameworkCore;
using SqlAgent.Core;

namespace SqlAgent.Storage;

/// <summary>One live table with its effective visibility for a connection.</summary>
public record TableVisibility(string Schema, string Table, bool IsVisible);

/// <summary>
/// Read/write side of per-table visibility (CD-50). <see cref="SchemaService"/> only ever exposes the
/// already-filtered schema, so the config UI needs this to see <em>every</em> live table — including the
/// hidden ones — and toggle them. A table with no <see cref="TablePolicy"/> row defaults to visible.
/// </summary>
public class TablePolicyService(
    DatabaseConnectionService connections, IDatabaseProviderRegistry providers, SqlAgentDbContext db)
{
    /// <summary>Lists every live table with its effective visibility, or null if the connection / its secret is missing.</summary>
    public async Task<IReadOnlyList<TableVisibility>?> ListAsync(Guid connectionId, CancellationToken ct = default)
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

        return schema.Tables
            .Select(t => new TableVisibility(t.Schema, t.Name, !hidden.Contains((t.Schema, t.Name))))
            .ToList();
    }

    /// <summary>Upserts one table's visibility flag. Returns false if the connection does not exist.</summary>
    public async Task<bool> SetVisibilityAsync(
        Guid connectionId, string schema, string table, bool isVisible, CancellationToken ct = default)
    {
        if (await connections.GetAsync(connectionId, ct) is null) return false;

        var policy = await db.TablePolicies.FirstOrDefaultAsync(
            p => p.DatabaseConnectionId == connectionId && p.SchemaName == schema && p.TableName == table, ct);
        if (policy is null)
        {
            policy = new TablePolicy
            {
                Id = Guid.NewGuid(),
                DatabaseConnectionId = connectionId,
                SchemaName = schema,
                TableName = table,
            };
            db.TablePolicies.Add(policy);
        }
        policy.IsVisible = isVisible;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
