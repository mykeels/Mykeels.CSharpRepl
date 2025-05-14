using Spectre.Console;

namespace CSharpRepl.Logging;

public static class JsonLogger
{
    public static string LogSuccess(string message, object result)
    {
        Console.WriteLine($"<< {message}");
        string output = Newtonsoft.Json.JsonConvert.SerializeObject(
            result
        );
        AnsiConsole.MarkupLine($"[green]>> {output.EscapeMarkup().Trim()}[/]");
        return output;
    }

    public static string LogError(string message, Exception exception, object _)
    {
        Console.WriteLine($"<< {message}");
        string output = Newtonsoft.Json.JsonConvert.SerializeObject(
            new {
                Error = exception
            }
        );
        AnsiConsole.MarkupLine($"[red]>> {output.EscapeMarkup().Trim()}[/]");
        return output;
    }
}