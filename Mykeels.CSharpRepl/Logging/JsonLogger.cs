using Newtonsoft.Json;
using Spectre.Console;
using Spectre.Console.Json;

namespace CSharpRepl.Logging;

public static class JsonLogger
{
    public static string LogSuccess(string message, object result)
    {
        Console.WriteLine($"<< {message}");
        string output = JsonConvert.SerializeObject(result, Formatting.Indented);
        WriteHighlightedJson(output, isError: false);
        return output;
    }

    public static string LogError(string message, Exception exception, object _)
    {
        Console.WriteLine($"<< {message}");
        string output = JsonConvert.SerializeObject(new { Error = exception }, Formatting.Indented);
        WriteHighlightedJson(output, isError: true);
        return output;
    }

    private static void WriteHighlightedJson(string json, bool isError)
    {
        try
        {
            var jsonText = new JsonText(json)
                .MemberColor(Color.LightSkyBlue1)
                .StringColor(Color.LightGreen)
                .NumberColor(Color.Orange1)
                .BooleanColor(Color.Pink1)
                .NullColor(Color.Grey)
                .BracesColor(Color.Grey)
                .BracketColor(Color.Grey)
                .ColonColor(Color.Grey)
                .CommaColor(Color.Grey);

            var panel = new Panel(jsonText)
            {
                Header = new PanelHeader(isError ? "[red]>>[/]" : "[green]>>[/]", Justify.Left),
                BorderStyle = new Style(isError ? Color.Red : Color.Green),
                Expand = true,
            };
            AnsiConsole.Write(panel);
        }
        catch
        {
            var color = isError ? "red" : "green";
            AnsiConsole.MarkupLine($"[{color}]>> {json.EscapeMarkup().Trim()}[/]");
        }
    }
}