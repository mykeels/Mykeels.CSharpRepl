using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using System.Text.Json;
using ModelContextProtocol;

namespace Mykeels.CSharpRepl;

public static class McpServer
{
    private static string _serverName = "Mykeels.CSharpRepl";
    public static async Task Run(Type globalsType, string? serverName = null)
    {
        _serverName = serverName ?? "Mykeels.CSharpRepl";
        await using IMcpServer server = McpServerFactory.Create(
            new StdioServerTransport(_serverName),
            GetMcpServerOptions(globalsType)
        );
        await server.RunAsync();
    }

    public static McpServerOptions GetMcpServerOptions(Type globalsType)
    {
        McpServerOptions options = new()
        {
            ServerInfo = new Implementation() { 
                Name = _serverName, 
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
                                ListMembersTool.Initialize(globalsType),
                                InvokeTool.Initialize(globalsType),
                            ]
                        }),

                    CallToolHandler = async (request, cancellationToken) =>
                    {
                        return await Handle(request, new Dictionary<string, Func<RequestContext<CallToolRequestParams>, Task<CallToolResponse>>>()
                        {
                            { ListMembersTool.Name, ListMembersTool.Handle },
                            { InvokeTool.Name, InvokeTool.Handle },
                        });
                    },
                }
            },
        };
        return options;
    }

    private static async Task<CallToolResponse> Handle(RequestContext<CallToolRequestParams> request, Dictionary<string, Func<RequestContext<CallToolRequestParams>, Task<CallToolResponse>>> handlers)
    {
        foreach (var handler in handlers)
        {
            if (request.Params?.Name == handler.Key)
            {
                return await handler.Value(request);
            }
        }
        throw new McpException($"Unknown tool: '{request.Params?.Name}'");
    }
}