using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SqlAgent.Core;

namespace SqlAgent.Storage;

/// <summary>
/// Produces the policy-filtered schema description handed to the LLM: extracts the live schema via the
/// connection's provider, then omits every table a <see cref="TablePolicy"/> marks invisible (CD-50 T4).
/// Tables with no policy default to visible.
///
/// CD-51 Story 1.5 adds caching: <see cref="RefreshAsync"/> stores the filtered description in
/// <see cref="SchemaCache"/>, and <see cref="GetOrRefreshAsync"/> reuses it (first-load populate, then reuse)
/// so the NL-to-SQL prompt path doesn't re-hit the live database on every request. The cache stores the
/// already-filtered schema, so a hidden table never reaches the cache; <see cref="InvalidateAsync"/> drops the
/// row when visibility changes so a now-hidden table can't survive in a stale cache entry.
/// </summary>
public class SchemaService(
    DatabaseConnectionService connections, IDatabaseProviderRegistry providers, SqlAgentDbContext db)
{
    /// <summary>
    /// Soft budget for the cached description (Story 1.5: "compact enough to fit within LLM context limits").
    /// The schema is cached minified (no indentation) regardless of size; this constant documents the size at
    /// which a typical schema stops fitting a model's context comfortably and the deferred selective-inclusion
    /// work (CD-51 risk: schema summarization) would need to kick in.
    /// ponytail: no summarization yet — the architect deferred automatic handling; we cache compact and document the ceiling.
    /// </summary>
    public const int MaxSchemaDescriptionBytes = 256 * 1024;

    // Minified by default (no WriteIndented): the cached JSON IS the compact DDL description for the LLM.
    private static readonly JsonSerializerOptions CompactJson = new(JsonSerializerDefaults.Web);

    /// <summary>Live-extracts the visible schema for a saved connection (no caching). Returns null if the
    /// connection or its secret is missing. This is the always-fresh path used by describe_schema.</summary>
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

    /// <summary>
    /// Re-extracts, filters, and stores the schema description in <see cref="SchemaCache"/> (manual refresh).
    /// Overwrites any prior cached row for the connection. Returns the filtered schema, or null if the
    /// connection/secret is missing.
    /// </summary>
    public async Task<DatabaseSchema?> RefreshAsync(Guid connectionId, CancellationToken ct = default)
    {
        var schema = await GetVisibleSchemaAsync(connectionId, ct);
        if (schema is null) return null;

        var json = JsonSerializer.Serialize(schema, CompactJson);
        var existing = await db.SchemaCaches.FirstOrDefaultAsync(c => c.DatabaseConnectionId == connectionId, ct);
        if (existing is null)
        {
            db.SchemaCaches.Add(new SchemaCache
            {
                Id = Guid.NewGuid(),
                DatabaseConnectionId = connectionId,
                FilteredSchemaJson = json,
                SchemaHash = Hash(json),
                GeneratedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.FilteredSchemaJson = json;
            existing.SchemaHash = Hash(json);
            existing.GeneratedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return schema;
    }

    /// <summary>
    /// Returns the cached filtered schema if present; otherwise refreshes (first-load populate) and caches it.
    /// This is the reuse path for the NL-to-SQL prompt builder — it avoids a live extraction per request.
    /// Returns null if the connection/secret is missing.
    /// </summary>
    public async Task<DatabaseSchema?> GetOrRefreshAsync(Guid connectionId, CancellationToken ct = default)
    {
        var cached = await db.SchemaCaches
            .Where(c => c.DatabaseConnectionId == connectionId)
            .Select(c => c.FilteredSchemaJson)
            .FirstOrDefaultAsync(ct);

        return cached is not null
            ? JsonSerializer.Deserialize<DatabaseSchema>(cached, CompactJson)
            : await RefreshAsync(connectionId, ct);
    }

    /// <summary>Drops the cached description for a connection. Called when visibility changes so a now-hidden
    /// table cannot survive in a stale cache entry (the next read re-extracts under the new policy).</summary>
    public async Task InvalidateAsync(Guid connectionId, CancellationToken ct = default)
    {
        await db.SchemaCaches
            .Where(c => c.DatabaseConnectionId == connectionId)
            .ExecuteDeleteAsync(ct);
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
