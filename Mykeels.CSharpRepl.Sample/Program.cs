using CSharpRepl.Services;
using Mykeels.CSharpRepl;
using Mykeels.CSharpRepl.Sample;

if (args.Contains("repl"))
{
    await Repl.Run(
        commands: [
            "using static Mykeels.CSharpRepl.Sample.ScriptGlobals;",
            "using Mykeels.CSharpRepl.Sample;"
        ]
    );
}
else
{
    await McpServer.Run(typeof(ScriptGlobals));
}
