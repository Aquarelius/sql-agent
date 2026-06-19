using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace SqlAgent.Storage;

/// <summary>
/// Stores connection-string secrets encrypted at rest, keyed by an opaque reference.
/// Callers (e.g. <see cref="DatabaseConnectionService"/>) hold only the reference, never the value.
/// </summary>
public interface ISecretStore
{
    Task SetAsync(string reference, string secret, CancellationToken ct = default);
    Task<string?> GetAsync(string reference, CancellationToken ct = default);
    Task DeleteAsync(string reference, CancellationToken ct = default);
}

/// <summary>
/// v1 OS-backed secret store: encrypts with Windows DPAPI (CurrentUser scope) and persists the
/// cipher blob in the local SQLite store. Windows-only — see <see cref="InMemorySecretStore"/> for
/// tests and non-Windows hosts.
/// </summary>
[SupportedOSPlatform("windows")]
public class DpapiSecretStore(SqlAgentDbContext db) : ISecretStore
{
    // ponytail: no extra entropy/salt for v1; DPAPI CurrentUser scope is the trust boundary.
    // Add per-secret entropy if a shared-machine threat model appears.
    public async Task SetAsync(string reference, string secret, CancellationToken ct = default)
    {
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);
        var existing = await db.Secrets.FindAsync([reference], ct);
        if (existing is null)
            db.Secrets.Add(new Secret { Reference = reference, Cipher = cipher });
        else
            existing.Cipher = cipher;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetAsync(string reference, CancellationToken ct = default)
    {
        var s = await db.Secrets.FindAsync([reference], ct);
        if (s is null) return null;
        var plain = ProtectedData.Unprotect(s.Cipher, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }

    public async Task DeleteAsync(string reference, CancellationToken ct = default)
    {
        var s = await db.Secrets.FindAsync([reference], ct);
        if (s is null) return;
        db.Secrets.Remove(s);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Non-persistent secret store for tests and non-Windows hosts. Keeps secrets in memory only;
/// nothing is written to disk. Not for production secret storage.
/// </summary>
public class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = [];

    public Task SetAsync(string reference, string secret, CancellationToken ct = default)
    {
        _secrets[reference] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string reference, CancellationToken ct = default)
        => Task.FromResult(_secrets.TryGetValue(reference, out var v) ? v : null);

    public Task DeleteAsync(string reference, CancellationToken ct = default)
    {
        _secrets.Remove(reference);
        return Task.CompletedTask;
    }
}
