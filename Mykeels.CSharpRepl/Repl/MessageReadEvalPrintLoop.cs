// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.Roslyn.Scripting;
using Newtonsoft.Json;

namespace Mykeels.CSharpRepl;

/// <summary>
/// Read/eval/print loop for message-based consoles (<see cref="IConsoleEx.IsInteractive"/> is
/// <see langword="false"/>) — e.g. Slack, where input only ever arrives as a whole submitted message rather
/// than key-by-key. Unlike <see cref="ReadEvalPrintLoop"/>, this does not use <see cref="PrettyPrompt.Prompt"/>,
/// since there's no notion of live, character-by-character editing over a message transport.
/// </summary>
internal sealed class MessageReadEvalPrintLoop(IConsoleEx console, RoslynServices roslyn)
{
    private static readonly string[] HelpCommands = ["help", "#help", "?"];

    public async Task RunAsync(Configuration config, Action<RoslynServices>? onLoad = null)
    {
        console.WriteLine($"Welcome to the {config.ApplicationName} REPL (Read Eval Print Loop)!");
        console.WriteLine("Send C# expressions and statements as messages to evaluate them.");
        console.WriteLine($"Send {Help} to learn more, or {Exit} to end this session.");
        console.WriteLine(string.Empty);
        await FlushAsync().ConfigureAwait(false);

        await ReadEvalPrintLoop.Preload(roslyn, console, config).ConfigureAwait(false);
        await FlushAsync().ConfigureAwait(false);

        onLoad?.Invoke(roslyn);

        while (true)
        {
            var message = await ReadLineAsync(CancellationToken.None).ConfigureAwait(false);
            if (message is null)
            {
                break;
            }

            var commandText = message.Trim();
            if (commandText.Length == 0)
            {
                continue;
            }

            var lowerCommandText = commandText.ToLowerInvariant();

            if (lowerCommandText == "exit")
            {
                break;
            }
            if (lowerCommandText == "clear")
            {
                console.WriteLine("`clear` is not applicable in a message-based session.");
                await FlushAsync().ConfigureAwait(false);
                continue;
            }
            if (HelpCommands.Contains(lowerCommandText))
            {
                PrintHelp(config.GlobalsType);
                await FlushAsync().ConfigureAwait(false);
                continue;
            }

            var result = await roslyn
                .EvaluateAsync(commandText, config.LoadScriptArgs, CancellationToken.None)
                .ConfigureAwait(false);

            await PrintResultAsync(result).ConfigureAwait(false);
            await FlushAsync().ConfigureAwait(false);
        }

        await FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Prints a successful evaluation's return value as indented JSON — the most legible, unambiguous way to
    /// show a value over a message transport with no live syntax highlighting or table rendering, and
    /// consistent with how this project's <c>JsonLogger</c> already treats results for non-interactive
    /// consumption elsewhere (just indented here, since that's for a human reading a chat thread, not a log).
    /// Errors and cancellation still go through the shared, richer <see cref="ReadEvalPrintLoop.PrintAsync"/>.
    /// </summary>
    private async Task PrintResultAsync(EvaluationResult result)
    {
        if (result is not EvaluationResult.Success success)
        {
            // Deliberately omit commandText here: when it's non-empty and config.LogSuccess is set (the
            // default), PrintAsync would otherwise route output to Configuration.LogSuccess/JsonLogger instead
            // of `console` — and JsonLogger writes straight to the real process Console, not to whichever
            // IConsoleEx is in play. That's fine for a real terminal (same underlying console either way), but
            // silently drops output for a message-based console like Slack.
            await ReadEvalPrintLoop.PrintAsync(roslyn, console, result, Level.FirstSimple).ConfigureAwait(false);
            return;
        }

        if (success.ReturnValue.HasValue)
        {
            var json = JsonConvert.SerializeObject(success.ReturnValue.Value, Formatting.Indented);
            console.WriteLine(json);
        }
        console.WriteLine();
    }

    private Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (console is IAsyncLineConsole asyncConsole)
        {
            return asyncConsole.ReadLineAsync(cancellationToken);
        }

        // Fall back to a dedicated thread rather than blocking a thread-pool thread indefinitely.
        return Task.Factory.StartNew(
            console.ReadLine,
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
    }

    private Task FlushAsync()
    {
        return console is IAsyncLineConsole asyncConsole
            ? asyncConsole.FlushAsync(CancellationToken.None)
            : Task.CompletedTask;
    }

    private void PrintHelp(Type? globalsType)
    {
        console.WriteLine(
            $"""
Send C# code as a message and it will be evaluated and the result sent back.

Global Variables:
  - ScriptGlobals: All global variables and services are static properties of the ScriptGlobals class.

Send {Exit} to end this session.
"""
        );

        if (globalsType is not null)
        {
            console.WriteLine();
            console.WriteLine($"Available members ({globalsType.FullName}):");
            foreach (var component in Introspector.ListComponents(globalsType))
            {
                console.WriteLine($"  - {component}");
            }
        }
    }

    private static string Help => "\"help\"";
    private static string Exit => "\"exit\"";
}
