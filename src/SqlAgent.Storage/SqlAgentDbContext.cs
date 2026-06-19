using Microsoft.EntityFrameworkCore;

namespace SqlAgent.Storage;

/// <summary>Local SQLite configuration store for the SQL Agent (CD-50 ADR-0004).</summary>
public class SqlAgentDbContext(DbContextOptions<SqlAgentDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseConnection> DatabaseConnections => Set<DatabaseConnection>();
    public DbSet<TablePolicy> TablePolicies => Set<TablePolicy>();
    public DbSet<SchemaCache> SchemaCaches => Set<SchemaCache>();
    public DbSet<QueryAuditLog> QueryAuditLogs => Set<QueryAuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Secret> Secrets => Set<Secret>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<DatabaseConnection>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<TablePolicy>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DatabaseConnectionId, x.SchemaName, x.TableName }).IsUnique();
        });
        b.Entity<SchemaCache>().HasKey(x => x.Id);
        b.Entity<QueryAuditLog>().HasKey(x => x.Id);
        b.Entity<AppSetting>().HasKey(x => x.Key);
        b.Entity<Secret>().HasKey(x => x.Reference);
    }
}
