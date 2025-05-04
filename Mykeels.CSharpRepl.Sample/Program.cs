using CSharpRepl.Services;
using Mykeels.CSharpRepl;
using Spectre.Console;

await Repl.Run(
    new Configuration(
        references: AppDomain
            .CurrentDomain.GetAssemblies()
            .Select(a => $"{a.GetName().Name}.dll")
            .ToArray(),
        usings: ["System", "System.Collections.Generic", "System.Linq"]
    )
);
