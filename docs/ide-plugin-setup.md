# Shared IDE plugin setup

SQL Agent IDE integrations are thin MCP host configurations around the existing
`SqlAgent.Api.Mcp` server. Hosts must not implement SQL validation, schema
filtering, secret access, row caps, or query execution themselves. Core owns
those behaviors and exposes the same three MCP tools to every supported host:

- `list_databases` - configured database connections, including provider and
  read-only flag.
- `describe_schema` - policy-filtered tables, columns, primary keys, and
  foreign keys. Tables hidden by Core policy are omitted.
- `query_database` - policy-validated SQL execution with row caps, elapsed time,
  and truncation metadata.

## Prerequisites

- .NET 10 SDK when launching from source, or a published `SqlAgent.Api.Mcp`
  executable when using packaged binaries.
- A SQL Agent SQLite configuration store with at least one configured
  connection.
- Windows for durable saved secrets in v1. The MCP transport can start on other
  platforms, but the current durable secret implementation uses Windows DPAPI
  current-user scope.
- A host that can launch stdio MCP servers. Claude Code, Gemini CLI, and Codex
  use this path.

## Build the MCP server

Build the MCP project before registering it with a host:

```bash
dotnet build src/SqlAgent.Api.Mcp/SqlAgent.Api.Mcp.csproj -c Release
```

MCP uses JSON-RPC over stdout. Prefer the built DLL or a published executable
over `dotnet run` for normal use so build output cannot corrupt the stream and
startup is faster.

## Configure `SQLAGENT_DB`

`SqlAgent.Api.Mcp` reads `SQLAGENT_DB` to choose the SQLite configuration store:

```bash
SQLAGENT_DB=/absolute/path/to/sqlagent.db
```

If `SQLAGENT_DB` is not set, the server uses `sqlagent.db` in the process
working directory. Use an absolute path when the host starts from a different
directory than the SQL Agent repo or desktop app.

The MCP process calls `EnsureCreated` for the SQLite store at startup. It does
not seed database connections; configure connections through the SQL Agent Core
or WPF client first.

## Host connection paths

| Host | Supported v1 path | Notes |
|------|-------------------|-------|
| Claude Code | Project `.mcp.json` or `claude mcp add` | See `packaging/claude-code/README.md`. |
| Gemini CLI | `mcpServers` entry in user or project `.gemini/settings.json` | Direct stdio MCP; no REST wrapper needed. |
| Codex CLI | `[mcp_servers.sql-agent]` in user or project `.codex/config.toml`, or `codex mcp add` | Use direct MCP configuration for v1; any future Codex package should still wrap this server. |

All supported hosts should point at the same MCP server and the same
`SQLAGENT_DB` store. Host packages are configuration and documentation only.

## Stable responses and errors

All tool responses include `ok`. Failed calls return stable `error_code` and
`error_message` fields instead of requiring hosts to parse prose.

Common stable error codes:

- `invalid_database_id`
- `connection_not_found`
- `connection_secret_missing`
- `schema_extraction_error`
- `policy_denied_readonly`
- `policy_denied_hidden_table`
- `execution_timeout`
- `execution_canceled`
- `execution_error`

Core may also return narrower `policy_denied_*` validation codes for malformed,
multi-statement, or unsupported SQL. Hosts should display the code and message
unchanged.

## Security and policy boundaries

Read-only enforcement, table visibility, query timeout behavior, row caps, and
audit logging are enforced in Core before results reach the MCP host. A host
integration is correct when it forwards calls to `SqlAgent.Api.Mcp` and leaves
the response shape untouched.

Do not expose the local named-pipe API to IDE plugins for v1. That API includes
configuration operations for the WPF client and is broader than the IDE plugin
tool surface.

## Platform limitations

- Durable saved secrets are Windows-only in v1 because `DpapiSecretStore` uses
  Windows DPAPI current-user scope.
- Linux and macOS are suitable for transport experiments, but saved connection
  secrets are not durable there until a non-DPAPI secret store is added.
- Windows services must run under the same Windows account that created the
  secrets, otherwise DPAPI cannot decrypt them.

## Troubleshooting

- **Host cannot discover tools:** build the MCP project first and point the host
  at the built DLL or published executable, not an unbuilt path.
- **JSON-RPC or stream errors:** confirm nothing writes non-MCP output to
  stdout. The server sends logs to stderr; build output from `dotnet run` is the
  usual source of stdout noise.
- **No databases returned:** `SQLAGENT_DB` points at an empty or wrong SQLite
  store. Use an absolute path and confirm connections exist.
- **`connection_secret_missing`:** the connection metadata exists but Core
  cannot retrieve the saved secret. Re-save the connection from the same Windows
  account that runs the MCP host.
- **Write queries are denied:** expected on read-only connections. Core returns
  `policy_denied_readonly`.
- **A table is missing or denied:** expected when table visibility hides it.
  Core omits hidden tables from `describe_schema` and denies direct queries with
  `policy_denied_hidden_table`.
