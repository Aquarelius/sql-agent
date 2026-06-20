# Sql Agent

AI Agent for access to SQL databases with controlled access

## Local host

Run the v1 console host with:

```bash
dotnet run --project src/SqlAgent.Host/SqlAgent.Host.csproj
```

The host creates the local SQLite store on startup. Override the default
`Data Source=sqlagent.db` store with:

```bash
SqlAgent__Storage__ConnectionString="Data Source=/path/to/sqlagent.db" dotnet run --project src/SqlAgent.Host/SqlAgent.Host.csproj
```

Windows service and systemd packaging examples are in `packaging/`. Operator
startup, fixture, and troubleshooting notes are in `docs/runbook.md`.

## Build and tests

```bash
dotnet restore SqlAgent.slnx
dotnet build SqlAgent.slnx --configuration Release --no-restore
dotnet test SqlAgent.slnx --configuration Release --no-build
```

Provider integration tests are opt-in. Start local fixtures from
`tests/fixtures/docker-compose.yml`, export `SQLAGENT_TEST_POSTGRES` and
`SQLAGENT_TEST_SQLSERVER`, then run:

```bash
dotnet test tests/SqlAgent.Tests/SqlAgent.Tests.csproj --filter ProviderIntegrationTests
```
