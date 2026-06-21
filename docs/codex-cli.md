# Codex CLI setup

Codex CLI supports MCP servers directly. The SQL Agent Codex v1 integration is a
configuration entry that launches `SqlAgent.Api.Mcp`; any future Codex package
should still wrap the same MCP server. Shared prerequisites, tool contracts,
errors, and troubleshooting live in [`ide-plugin-setup.md`](ide-plugin-setup.md).

## Configuration

Add `sql-agent` to user-level `~/.codex/config.toml` or a project-level
`.codex/config.toml`:

```toml
[mcp_servers.sql-agent]
command = "dotnet"
args = [
  "src/SqlAgent.Api.Mcp/bin/Release/net10.0/SqlAgent.Api.Mcp.dll"
]
cwd = "C:\\path\\to\\sql-agent"
env = { SQLAGENT_DB = "C:\\path\\to\\sqlagent.db" }
```

Or register it with the CLI:

```bash
codex mcp add sql-agent -- dotnet src/SqlAgent.Api.Mcp/bin/Release/net10.0/SqlAgent.Api.Mcp.dll
```

Use an absolute `SQLAGENT_DB` path. For packaged installs, point `command` at the
published `SqlAgent.Api.Mcp` executable instead of `dotnet`.

## Smoke test

1. Start a recent Codex CLI version that supports MCP config loading.
2. Confirm the `sql-agent` MCP server is loaded in the session.
3. Ask Codex to list databases and confirm it calls `list_databases`.
4. Ask it to describe a returned database id and confirm hidden tables are
   absent from `describe_schema`.
5. Ask it to run `SELECT 1` and confirm `query_database` returns rows and
   metadata.
6. On a read-only connection, ask it to run an `UPDATE`. Core should deny the
   call with `policy_denied_readonly`.

## Unsupported paths

Do not build a separate Codex query adapter for v1. Direct MCP configuration is
the supported path for this release. If an older Codex build does not load MCP
servers in a particular mode, upgrade Codex and rerun the smoke test before
filing a SQL Agent issue.
