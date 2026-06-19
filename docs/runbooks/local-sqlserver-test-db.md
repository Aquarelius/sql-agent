# Local SQL Server Test Database

This runbook starts a local SQL Server container and creates an empty `SqlAgentTest` database.

## Start

Set a local-only SA password through the environment. Do not commit it or post it in issues.

```powershell
$env:SQL_AGENT_TEST_DB_PASSWORD = "<local-strong-password>"
$env:SQL_AGENT_TEST_DB_PORT = "14333"
$env:SQL_AGENT_TEST_DB_NAME = "SqlAgentTest"
docker compose -f docker-compose.test-db.yml up -d
```

## Connection String

```text
Server=localhost,14333;Database=SqlAgentTest;User Id=sa;Password=<local-strong-password>;Encrypt=True;TrustServerCertificate=True;
```

## Verify

```powershell
docker exec sql-agent-test-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $env:SQL_AGENT_TEST_DB_PASSWORD -C -Q "SELECT name FROM sys.databases WHERE name = 'SqlAgentTest';"
```

## Stop

```powershell
docker compose -f docker-compose.test-db.yml down
```

