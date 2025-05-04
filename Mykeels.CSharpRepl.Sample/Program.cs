using CSharpRepl.Services;
using Mykeels.CSharpRepl;

await Repl.Run(
    commands: [
        "using static Mykeels.CSharpRepl.Sample.ScriptGlobals;"
    ]
);
