# ADR 0001: MCP-first IDE plugin architecture

## Status

Accepted

## Context

CD-54 requires IDE integrations for Claude Code, Gemini CLI, and Codex that let
developers query configured databases from their normal coding environments.
SQL Agent already has `SqlAgent.Api.Mcp`, a stdio MCP server exposing the
required `list_databases`, `describe_schema`, and `query_database` tools.

The WPF local named-pipe API is broader than the IDE plugin use case because it
includes configuration operations. Duplicating SQL policy, schema visibility,
secret access, row caps, or query execution in host-specific packages would risk
contract drift and inconsistent enforcement.

## Decision

Use MCP as the canonical IDE plugin API for v1. Claude Code, Gemini CLI, and
Codex integrations configure or launch the existing `SqlAgent.Api.Mcp` stdio
server wherever the host supports MCP.

Host-specific packages are distribution and documentation layers only. Core
remains the sole owner of SQL policy, schema visibility, execution, secrets, row
caps, timeouts, and auditing.

## Consequences

- The three IDE hosts share one tool contract and one implementation path.
- No database provider, policy, secret, or execution logic is duplicated in host
  packages.
- Host setup work is mostly documentation and configuration.
- Future REST or named-pipe adapters may be added only for hosts that cannot
  consume MCP, and they must preserve the current MCP response contract.
- v1 durable saved secrets remain Windows-only until a non-DPAPI secret store is
  implemented.
