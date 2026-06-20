using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlAgent.Api.Local;
using SqlAgent.Core;
using SqlAgent.Providers.Postgres;
using SqlAgent.Providers.SqlServer;
using SqlAgent.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddWindowsService(options => options.ServiceName = "SQL Agent")
    .AddSystemd();

builder.Services.AddDbContext<SqlAgentDbContext>(options =>
{
    var connectionString = builder.Configuration["SqlAgent:Storage:ConnectionString"]
        ?? "Data Source=sqlagent.db";
    options.UseSqlite(connectionString);
});

builder.Services.AddSingleton<IDatabaseProvider, SqlServerProvider>();
builder.Services.AddSingleton<IDatabaseProvider, PostgresProvider>();
builder.Services.AddSingleton<IDatabaseProviderRegistry, DatabaseProviderRegistry>();
builder.Services.AddScoped<DatabaseConnectionService>();
builder.Services.AddScoped<QueryExecutionService>();
builder.Services.AddScoped<ConnectionTester>();
builder.Services.AddScoped<SchemaService>();
builder.Services.AddScoped<TablePolicyService>();
builder.Services.AddScoped<LocalApiDispatcher>();

if (OperatingSystem.IsWindows())
    builder.Services.AddScoped<ISecretStore, DpapiSecretStore>();
else
    builder.Services.AddSingleton<ISecretStore, InMemorySecretStore>();

builder.Services.AddHostedService<SqlAgentWorker>();

await builder.Build().RunAsync();

internal sealed class SqlAgentWorker(
    IServiceProvider services,
    ILogger<SqlAgentWorker> logger,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SqlAgentDbContext>();
            await db.Database.EnsureCreatedAsync(stoppingToken);

            var connections = scope.ServiceProvider.GetRequiredService<DatabaseConnectionService>();
            var configuredConnections = (await connections.ListAsync(stoppingToken)).Count;
            logger.LogInformation("SQL Agent host started with {ConnectionCount} configured connection(s).", configuredConnections);
        }

        // Serve the local named-pipe API that the WPF client talks to (CD-50 T8/T9). The server handles one
        // connection at a time, so we dispose the previous request scope when the next connection asks for a
        // dispatcher — at most one scope is live, no leak.
        // ponytail: serial-scope reuse relies on NamedPipeApiServer serving connections one at a time.
        IServiceScope? connectionScope = null;
        var pipe = new NamedPipeApiServer(() =>
        {
            connectionScope?.Dispose();
            connectionScope = services.CreateScope();
            return connectionScope.ServiceProvider.GetRequiredService<LocalApiDispatcher>();
        });

        try
        {
            await pipe.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("SQL Agent host stopping.");
        }
        finally
        {
            connectionScope?.Dispose();
            lifetime.StopApplication();
        }
    }
}
