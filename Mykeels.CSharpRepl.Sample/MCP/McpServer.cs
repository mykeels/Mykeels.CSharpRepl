using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Mykeels.CSharpRepl.MCP;

public static class McpServer
{
    public static async Task Run(Type globalsType)
    {
        var builder = Host.CreateApplicationBuilder(new string[] { });

        builder.Logging.AddConsole(consoleLogOptions =>
        {
            // Configure all logs to go to stderr
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(); // Registers all [McpServerToolType] in the assembly

        await builder.Build().RunAsync();
    }
}