// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CSharpRepl.Services;

public interface IConsoleEx : IAnsiConsole
{
    IConsole PrettyPromptConsole { get; }

    /// <summary>
    /// Whether this console supports character-by-character interactive editing (syntax highlighting as you
    /// type, tab completion, etc.) via <see cref="PrettyPrompt"/>. Consoles backed by a message-based transport
    /// (e.g. Slack, where input only ever arrives as a whole submitted message) should override this to
    /// <see langword="false"/> so that <c>Repl.Run</c> drives them with a simpler read/eval/print loop instead
    /// of trying to hand them to <see cref="PrettyPrompt.Prompt"/>.
    /// </summary>
    bool IsInteractive => true;

    private IAnsiConsole AnsiConsole => this;

    void Write(string text) => AnsiConsole.Write(text);
    void Write(FormattedString text) => PrettyPromptConsole.Write(text);

    void WriteLine(string text) => AnsiConsole.WriteLine(text);
    void WriteLine() => AnsiConsole.WriteLine();
    void WriteLine(FormattedString text) => PrettyPromptConsole.WriteLine(text);

    void WriteError(string text)
    {
        if (PrettyPromptConsole.IsErrorRedirected)
        {
            PrettyPromptConsole.WriteError(text);
        }
        else
        {
            //AnsiConsole is smarter about word wrapping
            Write(text);
        }
    }

    /// <param name="text">Text written to error stream (used only if error stream is redirected).</param>
    void WriteError(IRenderable renderable, string text)
    {
        if (PrettyPromptConsole.IsErrorRedirected)
        {
            PrettyPromptConsole.WriteError(text);
        }
        else
        {
            //AnsiConsole is smarter about word wrapping
            Write(renderable);
        }
    }

    /// <param name="text">Text written to error stream (used only if error stream is redirected).</param>
    void WriteErrorLine(IRenderable renderable, string text)
    {
        if (PrettyPromptConsole.IsErrorRedirected)
        {
            PrettyPromptConsole.WriteErrorLine(text);
        }
        else
        {
            //AnsiConsole is smarter about word wrapping
            Write(renderable);
            WriteLine();
        }
    }

    void WriteErrorLine(string text)
    {
        if (PrettyPromptConsole.IsErrorRedirected)
        {
            PrettyPromptConsole.WriteErrorLine(text);
        }
        else
        {
            //AnsiConsole is smarter about word wrapping
            WriteLine(text);
        }
    }

    string? ReadLine();
}
