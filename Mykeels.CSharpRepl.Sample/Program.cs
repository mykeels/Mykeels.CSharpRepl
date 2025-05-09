// using CSharpRepl.Services;
// using Mykeels.CSharpRepl;

// await Repl.Run(
//     commands: [
//         "using static Mykeels.CSharpRepl.Sample.ScriptGlobals;"
//     ]
// );

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

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