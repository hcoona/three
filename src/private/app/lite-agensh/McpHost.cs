using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LiteAgensh;

public static class McpHost
{
    public static async Task RunAsync(RunDatabase database, string actor, CancellationToken cancellationToken = default)
    {
        if (!database.Members.Contains(actor, StringComparer.Ordinal))
        {
            throw new ArgumentException("The MCP helper requires a known bound member.", nameof(actor));
        }

        LocalTools tools = new(database);
        McpServerOptions options = new()
        {
            ServerInfo = new Implementation
            {
                Name = "LiteAgensh",
                Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = [.. ToolCatalog.Definitions],
                }),
                CallToolHandler = async (request, token) =>
                {
                    string name = request.Params?.Name
                        ?? throw new McpProtocolException("A tool name is required.", McpErrorCode.InvalidParams);
                    JsonObject arguments = request.Params?.Arguments is { } values
                        ? Json.SerializePayload(values)
                        : [];
                    JsonObject result = await tools.InvokeAsync(actor, name, arguments, token).ConfigureAwait(false);
                    return new CallToolResult
                    {
                        IsError = result["ok"]?.GetValue<bool>() != true,
                        Content = [new TextContentBlock { Text = result.ToJsonString(Json.Options) }],
                    };
                },
            },
        };
        await using McpServer server = McpServer.Create(new StdioServerTransport("LiteAgensh"), options);
        await server.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
