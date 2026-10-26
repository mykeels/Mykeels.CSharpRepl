using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Json;
using Spectre.Console.Rendering;

namespace Mykeels.CSharpRepl;

/// <summary>
/// Interactive scrollable view of a C# request and its expanded, syntax-highlighted JSON result.
/// </summary>
internal static class ResultHistoryViewer
{
    private static readonly Style PunctuationStyle = new(Color.Grey);
    private static readonly Style KeyStyle = new(Color.LightSkyBlue1);
    private static readonly Style StringStyle = new(Color.LightGreen);
    private static readonly Style NumberStyle = new(Color.Orange1);
    private static readonly Style BooleanStyle = new(Color.Pink1);
    private static readonly Style NullStyle = new(Color.Grey);

    public static void Show(IAnsiConsole console, ResultHistory history)
    {
        if (history.Count == 0)
        {
            console.MarkupLine("[yellow]No result history yet. Evaluate an expression first.[/]");
            return;
        }

        // Open (or re-open) at the newest entry when not already browsing.
        if (history.BrowseIndex < 0)
            history.MoveOlder();

        var scrollOffset = 0;

        try
        {
            console
                .Live(BuildView(console, history, scrollOffset))
                .AutoClear(true)
                .Overflow(VerticalOverflow.Ellipsis)
                .Start(ctx =>
                {
                    while (true)
                    {
                        try
                        {
                            ctx.UpdateTarget(BuildView(console, history, scrollOffset));
                        }
                        catch (Exception ex)
                        {
                            ctx.UpdateTarget(BuildFallbackView(history, scrollOffset, ex));
                        }

                        var key = console.Input.ReadKey(intercept: true);
                        if (key is null)
                            continue;

                        var info = key.Value;
                        var ctrl = info.Modifiers.HasFlag(ConsoleModifiers.Control);

                        switch (info.Key)
                        {
                            case ConsoleKey.UpArrow when ctrl:
                                if (history.BrowseIndex > 0)
                                {
                                    history.MoveOlder();
                                    scrollOffset = 0;
                                }
                                break;

                            case ConsoleKey.DownArrow when ctrl:
                                if (history.BrowseIndex >= 0 && history.BrowseIndex < history.Count - 1)
                                {
                                    history.MoveNewer();
                                    scrollOffset = 0;
                                }
                                break;

                            case ConsoleKey.UpArrow:
                                scrollOffset = Math.Max(0, scrollOffset - 1);
                                break;

                            case ConsoleKey.DownArrow:
                                scrollOffset = Math.Min(MaxScroll(console, history), scrollOffset + 1);
                                break;

                            case ConsoleKey.PageUp:
                                scrollOffset = Math.Max(0, scrollOffset - JsonViewportHeight(console));
                                break;

                            case ConsoleKey.PageDown:
                                scrollOffset = Math.Min(
                                    MaxScroll(console, history),
                                    scrollOffset + JsonViewportHeight(console)
                                );
                                break;

                            case ConsoleKey.Home:
                                scrollOffset = 0;
                                break;

                            case ConsoleKey.End:
                                scrollOffset = MaxScroll(console, history);
                                break;

                            case ConsoleKey.Escape:
                            case ConsoleKey.Enter:
                            case ConsoleKey.Q:
                                history.ResetBrowse();
                                return;
                        }
                    }
                });
        }
        catch (Exception ex)
        {
            history.ResetBrowse();
            console.MarkupLine($"[red]Failed to open result history view:[/] {ex.Message.EscapeMarkup()}");
            try
            {
                console.Write(BuildFallbackView(history, scrollOffset: 0, error: null));
            }
            catch
            {
                // Last resort: plain text dump of the current entry.
                var entry = history.Current;
                if (entry is not null)
                {
                    console.WriteLine($"<< {entry.Request}");
                    console.WriteLine(entry.JsonResult);
                }
            }
        }
    }

    private static IRenderable BuildView(IAnsiConsole console, ResultHistory history, int scrollOffset)
    {
        var entry = history.Current
            ?? throw new InvalidOperationException("No history entry selected.");
        var position = history.BrowseIndex + 1;
        var total = history.Count;
        var jsonLines = SplitLines(entry.JsonResult);
        var viewportHeight = JsonViewportHeight(console);
        var maxScroll = Math.Max(0, jsonLines.Length - viewportHeight);
        scrollOffset = Math.Clamp(scrollOffset, 0, maxScroll);

        // Prefer JsonText (real JSON highlighting) when the full payload fits.
        // When scrolled, use style-based line rendering (no Spectre markup strings).
        IRenderable jsonBody;
        if (scrollOffset == 0 && jsonLines.Length <= viewportHeight)
        {
            jsonBody = TryCreateJsonText(entry.JsonResult)
                ?? HighlightedJsonLines(jsonLines, 0, viewportHeight);
        }
        else
        {
            jsonBody = HighlightedJsonLines(jsonLines, scrollOffset, viewportHeight);
        }

        var requestLine = new Text($"<< {entry.Request}", new Style(Color.Cyan1, decoration: Decoration.Bold));
        var scrollHint =
            jsonLines.Length > viewportHeight
                ? $"lines {scrollOffset + 1}-{Math.Min(scrollOffset + viewportHeight, jsonLines.Length)}/{jsonLines.Length}"
                : $"{jsonLines.Length} lines";

        var panel = new Panel(jsonBody)
        {
            Header = new PanelHeader(
                $"Result {position}/{total}  {scrollHint}",
                Justify.Left
            ),
            BorderStyle = new Style(entry.IsError ? Color.Red : Color.Green),
            Expand = true,
        };

        var footer = new Text(
            "Ctrl+↑/↓ history  ·  ↑/↓/PgUp/PgDn scroll  ·  Esc/Enter close",
            PunctuationStyle
        );

        return new Rows(requestLine, panel, footer);
    }

    private static IRenderable BuildFallbackView(ResultHistory history, int scrollOffset, Exception? error)
    {
        var entry = history.Current;
        if (entry is null)
            return new Text("No history entry selected.", new Style(Color.Yellow));

        var lines = SplitLines(entry.JsonResult);
        var body = string.Join(
            Environment.NewLine,
            lines.Skip(Math.Max(0, scrollOffset)).Take(40)
        );

        var rows = new List<IRenderable>
        {
            new Text($"<< {entry.Request}", new Style(Color.Cyan1)),
            new Text(body),
        };

        if (error is not null)
        {
            rows.Insert(
                0,
                new Text(
                    $"Display error (showing plain text): {error.Message}",
                    new Style(Color.Yellow)
                )
            );
        }

        rows.Add(new Text("Esc/Enter close", PunctuationStyle));
        return new Rows(rows);
    }

    private static JsonText? TryCreateJsonText(string json)
    {
        try
        {
            return new JsonText(json)
                .MemberColor(Color.LightSkyBlue1)
                .StringColor(Color.LightGreen)
                .NumberColor(Color.Orange1)
                .BooleanColor(Color.Pink1)
                .NullColor(Color.Grey)
                .BracesColor(Color.Grey)
                .BracketColor(Color.Grey)
                .ColonColor(Color.Grey)
                .CommaColor(Color.Grey);
        }
        catch
        {
            return null;
        }
    }

    private static IRenderable HighlightedJsonLines(string[] lines, int offset, int count)
    {
        var rows = lines
            .Skip(offset)
            .Take(count)
            .Select(CreateStyledLine)
            .ToArray();

        return rows.Length == 0
            ? new Text("(empty)", PunctuationStyle)
            : new Rows(rows);
    }

    /// <summary>
    /// Builds a styled line using Spectre Text/Paragraph APIs so JSON brackets
    /// never go through markup parsing (which treats ] as a control character).
    /// </summary>
    private static IRenderable CreateStyledLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return new Text(string.Empty);

        try
        {
            var paragraph = new Paragraph();
            foreach (var (text, style) in TokenizeJsonLine(line))
            {
                paragraph.Append(text, style);
            }
            return paragraph;
        }
        catch
        {
            // Never let a single bad line take down the viewer.
            return new Text(line);
        }
    }

    private static List<(string Text, Style? Style)> TokenizeJsonLine(string line)
    {
        var tokens = new List<(string, Style?)>();
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            if (char.IsWhiteSpace(c))
            {
                var start = i;
                while (i < line.Length && char.IsWhiteSpace(line[i]))
                    i++;
                tokens.Add((line[start..i], null));
                continue;
            }

            if (c is '{' or '}' or '[' or ']' or ',' or ':')
            {
                tokens.Add((c.ToString(), PunctuationStyle));
                i++;
                continue;
            }

            if (c == '"')
            {
                var start = i;
                i++;
                while (i < line.Length)
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        i += 2;
                        continue;
                    }
                    if (line[i] == '"')
                    {
                        i++;
                        break;
                    }
                    i++;
                }

                var quoted = line[start..i];
                var j = i;
                while (j < line.Length && char.IsWhiteSpace(line[j]))
                    j++;
                var isKey = j < line.Length && line[j] == ':';
                tokens.Add((quoted, isKey ? KeyStyle : StringStyle));
                continue;
            }

            if (char.IsDigit(c) || (c == '-' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
            {
                var start = i;
                i++;
                while (
                    i < line.Length
                    && (char.IsDigit(line[i]) || line[i] is '.' or 'e' or 'E' or '+' or '-')
                )
                    i++;
                tokens.Add((line[start..i], NumberStyle));
                continue;
            }

            if (TryReadKeyword(line, ref i, "true", out var booleanKeyword)
                || TryReadKeyword(line, ref i, "false", out booleanKeyword))
            {
                tokens.Add((booleanKeyword, BooleanStyle));
                continue;
            }

            if (TryReadKeyword(line, ref i, "null", out var nullKeyword))
            {
                tokens.Add((nullKeyword, NullStyle));
                continue;
            }

            tokens.Add((c.ToString(), null));
            i++;
        }

        return tokens;
    }

    private static bool TryReadKeyword(
        string line,
        ref int i,
        string keyword,
        out string matched
    )
    {
        matched = keyword;
        if (i + keyword.Length > line.Length)
            return false;
        if (!line.AsSpan(i, keyword.Length).Equals(keyword, StringComparison.Ordinal))
            return false;
        var end = i + keyword.Length;
        if (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
            return false;
        i = end;
        return true;
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

    private static int JsonViewportHeight(IAnsiConsole console)
    {
        // Leave room for request line, panel borders/header, footer, and prompt chrome.
        const int chrome = 8;
        var height = console.Profile.Height;
        if (height <= 0)
            height = 24;
        return Math.Max(5, height - chrome);
    }

    private static int MaxScroll(IAnsiConsole console, ResultHistory history)
    {
        var lines = SplitLines(history.Current?.JsonResult ?? string.Empty).Length;
        return Math.Max(0, lines - JsonViewportHeight(console));
    }
}
