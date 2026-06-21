using System.Reflection;
using ModelContextProtocol.Server;
using SqlAgent.Api.Mcp;

namespace SqlAgent.Tests;

/// <summary>
/// The Claude Code package (packaging/claude-code) documents exactly three tools. This guards that the
/// MCP surface the host registers does not silently drift from what the setup guide promises.
/// </summary>
public class DatabaseToolsContractTests
{
    [Fact]
    public void DatabaseTools_exposes_exactly_the_documented_tool_names()
    {
        var toolNames = typeof(DatabaseTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => n is not null)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(new[] { "describe_schema", "list_databases", "query_database" }, toolNames);
    }
}
