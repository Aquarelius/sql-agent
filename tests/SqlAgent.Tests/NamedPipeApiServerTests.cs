using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqlAgent.Api.Local;
using SqlAgent.Core;
using SqlAgent.Storage;

namespace SqlAgent.Tests;

/// <summary>
/// Transport-level check: the named-pipe server frames newline-delimited JSON and round-trips a real
/// request to a real <see cref="LocalApiDispatcher"/>. Windows-only (named pipes); the dispatcher's own
/// behavior is covered without a pipe in <see cref="LocalApiDispatcherTests"/>.
/// </summary>
public class NamedPipeApiServerTests
{
    private static LocalApiDispatcher BuildDispatcher()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new SqlAgentDbContext(new DbContextOptionsBuilder<SqlAgentDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        var connections = new DatabaseConnectionService(db, new InMemorySecretStore());
        var registry = new DatabaseProviderRegistry(Array.Empty<IDatabaseProvider>());
        return new LocalApiDispatcher(
            connections,
            new ConnectionTester(connections, registry),
            new SchemaService(connections, registry, db),
            new QueryExecutionService(connections, registry, db));
    }

    [Fact]
    public async Task Round_trips_a_request_over_the_pipe()
    {
        var pipeName = "SqlAgent.Test." + Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var server = new NamedPipeApiServer(BuildDispatcher, pipeName);
        var serverTask = server.RunAsync(cts.Token);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cts.Token);
        using var reader = new StreamReader(client, leaveOpen: true);
        await using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };

        var request = JsonSerializer.Serialize(new { op = "list_databases" }, LocalApiContract.Json);
        await writer.WriteLineAsync(request.AsMemory(), cts.Token);
        var responseLine = await reader.ReadLineAsync(cts.Token);

        Assert.NotNull(responseLine);
        var response = JsonSerializer.Deserialize<LocalApiResponse>(responseLine!, LocalApiContract.Json)!;
        Assert.True(response.Ok);
        Assert.Equal(LocalApiContract.Version, response.Version);
        Assert.Empty(response.Data!.Value.Deserialize<List<DatabaseDto>>(LocalApiContract.Json)!);

        cts.Cancel();
        await serverTask; // cancellation unwinds the serve loop and RunAsync returns cleanly
    }
}
