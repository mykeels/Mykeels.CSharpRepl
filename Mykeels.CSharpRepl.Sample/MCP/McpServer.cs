using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using System.Text.Json;
using ModelContextProtocol;

namespace Mykeels.CSharpRepl.MCP;

public static class McpServer
{
    private const string ServerName = "Mykeels.CSharpRepl.Sample";
    public static async Task Run(Type globalsType)
    {
        await using IMcpServer server = McpServerFactory.Create(
            new StdioServerTransport(ServerName),
            GetMcpServerOptions(globalsType)
        );
        await server.RunAsync();
    }

    public static McpServerOptions GetMcpServerOptions(Type globalsType)
    {
        McpServerOptions options = new()
        {
            ServerInfo = new Implementation() { 
                Name = ServerName, 
                Version = "1.0.0" 
            },
            Capabilities = new ServerCapabilities()
            {
                Tools = new ToolsCapability()
                {
                    ListToolsHandler = async (request, cancellationToken) =>
                        await Task.FromResult(new ListToolsResult()
                        {
                            Tools =
                            [
                                new Tool()
                                {
                                    Name = "echo",
                                    Description = "Echoes the input back to the client.",
                                    InputSchema = JsonSerializer.Deserialize<JsonElement>("""
                                        {
                                            "type": "object",
                                            "properties": {
                                                "message": {
                                                    "type": "string",
                                                    "description": "The input to echo back"
                                                }
                                            },
                                            "required": ["message"]
                                        }
                                        """),
                                }
                            ]
                        }),

                    CallToolHandler = async (request, cancellationToken) =>
                    {
                        if (request.Params?.Name == "echo")
                        {
                            if (request.Params.Arguments?.TryGetValue("message", out var message) is not true)
                            {
                                throw new McpException("Missing required argument 'message'");
                            }

                            return await Task.FromResult(new CallToolResponse()
                            {
                                Content = [new Content() { Text = $"Echo: {message}", Type = "text" }]
                            });
                        }

                        throw new McpException($"Unknown tool: '{request.Params?.Name}'");
                    },
                }
            },
        };
        return options;
    }
}