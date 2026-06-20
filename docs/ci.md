# CI

The GitHub Actions workflow in `.github/workflows/ci.yml` has two jobs:

- `build-test`: restores, builds, and runs the default test suite with .NET 10.
- `provider-fixtures`: starts PostgreSQL and SQL Server service containers and runs the opt-in provider integration tests.

Local equivalents:

```bash
dotnet restore SqlAgent.slnx
dotnet build SqlAgent.slnx --configuration Release --no-restore
dotnet test SqlAgent.slnx --configuration Release --no-build
```

Provider fixtures:

```bash
docker compose -f tests/fixtures/docker-compose.yml up -d
export SQLAGENT_TEST_POSTGRES='Host=localhost;Port=5432;Database=sqlagent;Username=sqlagent;Password=sqlagent_pw'
export SQLAGENT_TEST_SQLSERVER='Server=localhost,1433;Database=master;User Id=sa;Password=SqlAgent_pw1;TrustServerCertificate=True'
dotnet test tests/SqlAgent.Tests/SqlAgent.Tests.csproj --filter ProviderIntegrationTests
docker compose -f tests/fixtures/docker-compose.yml down
```

The integration tests return without assertions when their environment variable
is missing, so normal unit-test runs do not require Docker or database servers.
