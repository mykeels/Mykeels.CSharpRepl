using CSharpRepl.Services;
using Mykeels.CSharpRepl;
using Spectre.Console;

await Repl.Run(
    new Configuration(
        references: AppDomain
            .CurrentDomain.GetAssemblies()
            .Select(a => $"{a.GetName().Name}.dll")
            .ToArray(),
        usings: ["System", "System.Collections.Generic", "System.Linq"],
        applicationName: "Mykeels.CSharpRepl.Sample",
        logSuccess: (message, result) => {
            Console.WriteLine($"<< {message}");
            string output = Newtonsoft.Json.JsonConvert.SerializeObject(
                result
            );
            AnsiConsole.MarkupLine($"[green]>> {output.EscapeMarkup().Trim()}[/]");
        },
        logError: (message, exception, _) => {
            Console.WriteLine($"<< {message}");
            string output = Newtonsoft.Json.JsonConvert.SerializeObject(
                new {
                    Error = exception
                }
            );
            AnsiConsole.MarkupLine($"[red]>> {output.EscapeMarkup().Trim()}[/]");
        }
    )
);
