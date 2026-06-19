using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqlAgent.Core;
using SqlAgent.Storage;

namespace SqlAgent.Tests;

/// <summary>Returns a canned schema from <see cref="GetSchemaAsync"/>; no real database I/O.</summary>
file sealed class SchemaFakeProvider(DatabaseProviderType type, DatabaseSchema schema) : IDatabaseProvider
{
    public DatabaseProviderType ProviderType => type;

    public Task<ConnectionTestResult> TestConnectionAsync(string connectionString, CancellationToken ct = default)
        => Task.FromResult(ConnectionTestResult.Ok(null, 0));

    public Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken ct = default)
        => Task.FromResult(schema);
}

public class SchemaServiceTests
{
    private static readonly DatabaseSchema Sample = SchemaModel.Build(
        columns:
        [
            ("dbo", "Orders", "Id", "int", false),
            ("dbo", "Secret", "Id", "int", false),
        ],
        primaryKeys: [],
        foreignKeys:
        [
            ("dbo", "Orders", "SecretId", "dbo", "Secret", "Id"),
        ]);

    private static (SqlAgentDbContext db, DatabaseConnectionService svc, SqliteConnection conn) NewDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new SqlAgentDbContext(
            new DbContextOptionsBuilder<SqlAgentDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        return (db, new DatabaseConnectionService(db, new InMemorySecretStore()), conn);
    }

    [Fact]
    public async Task GetVisibleSchema_omits_tables_marked_invisible_and_their_inbound_fk()
    {
        var (db, svc, conn) = NewDb();
        var created = await svc.CreateAsync(
            new DatabaseConnectionInput("c", DatabaseProviderType.SqlServer, false), "Server=x");
        db.TablePolicies.Add(new TablePolicy
        {
            Id = Guid.NewGuid(), DatabaseConnectionId = created.Id,
            SchemaName = "dbo", TableName = "Secret", IsVisible = false,
        });
        await db.SaveChangesAsync();

        var providers = new DatabaseProviderRegistry(
            [new SchemaFakeProvider(DatabaseProviderType.SqlServer, Sample)]);
        var schema = await new SchemaService(svc, providers, db).GetVisibleSchemaAsync(created.Id);

        Assert.NotNull(schema);
        var table = Assert.Single(schema!.Tables);
        Assert.Equal("Orders", table.Name);
        Assert.Empty(table.ForeignKeys); // FK to hidden Secret dropped
        conn.Dispose();
    }

    [Fact]
    public async Task GetVisibleSchema_returns_all_tables_when_no_policy_hides_them()
    {
        var (db, svc, conn) = NewDb();
        var created = await svc.CreateAsync(
            new DatabaseConnectionInput("c", DatabaseProviderType.SqlServer, false), "Server=x");

        var providers = new DatabaseProviderRegistry(
            [new SchemaFakeProvider(DatabaseProviderType.SqlServer, Sample)]);
        var schema = await new SchemaService(svc, providers, db).GetVisibleSchemaAsync(created.Id);

        Assert.NotNull(schema);
        Assert.Equal(2, schema!.Tables.Count);
        conn.Dispose();
    }

    [Fact]
    public async Task GetVisibleSchema_returns_null_for_an_unknown_connection()
    {
        var (db, svc, conn) = NewDb();
        var providers = new DatabaseProviderRegistry(
            [new SchemaFakeProvider(DatabaseProviderType.SqlServer, Sample)]);

        Assert.Null(await new SchemaService(svc, providers, db).GetVisibleSchemaAsync(Guid.NewGuid()));
        conn.Dispose();
    }
}
