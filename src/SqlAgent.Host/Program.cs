using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("SQL Agent host stopping.");
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
