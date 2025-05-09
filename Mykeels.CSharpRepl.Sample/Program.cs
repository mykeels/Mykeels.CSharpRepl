// using CSharpRepl.Services;
// using Mykeels.CSharpRepl;

// await Repl.Run(
//     commands: [
//         "using static Mykeels.CSharpRepl.Sample.ScriptGlobals;"
//     ]
// );

using Mykeels.CSharpRepl.MCP;
using Mykeels.CSharpRepl.Sample;

await McpServer.Run(typeof(ScriptGlobals));