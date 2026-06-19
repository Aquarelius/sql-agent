using Microsoft.EntityFrameworkCore;

namespace SqlAgent.Storage;

/// <summary>Builds a ready-to-use SQLite-backed context. Schema is created on first use.</summary>
public static class StorageFactory
{
    public static DbContextOptions<SqlAgentDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<SqlAgentDbContext>().UseSqlite(connectionString).Options;

    /// <summary>Opens a file-backed store, e.g. <c>Data Source=sqlagent.db</c>, ensuring the schema exists.</summary>
    public static SqlAgentDbContext Open(string connectionString)
    {
        var db = new SqlAgentDbContext(Options(connectionString));
        db.Database.EnsureCreated();
        return db;
    }
}
