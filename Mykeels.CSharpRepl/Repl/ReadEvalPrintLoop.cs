using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.Services;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.Theming;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using Spectre.Console;

namespace Mykeels.CSharpRepl;

internal sealed class ReadEvalPrintLoop
{
    private readonly IConsoleEx console;
    private readonly RoslynServices roslyn;
    private readonly IPrompt prompt;
    private static Configuration config;

    public ReadEvalPrintLoop(IConsoleEx console, RoslynServices roslyn, IPrompt prompt)
    {
        this.console = console;
        this.roslyn = roslyn;
        this.prompt = prompt;
    }

    public async Task RunAsync(Configuration config, Action<RoslynServices>? onLoad = null)
    {
        ReadEvalPrintLoop.config = config;
        console.WriteLine($"Welcome to the {config.ApplicationName} REPL (Read Eval Print Loop)!");
        console.WriteLine(
            "Type C# expressions and statements at the prompt and press Enter to evaluate them."
        );
        console.WriteLine(
            $"Type {Help} to learn more, {Exit} to quit, and {Clear} to clear your terminal."
        );
        console.WriteLine(string.Empty);

        await Preload(roslyn, console, config).ConfigureAwait(false);

        onLoad?.Invoke(roslyn);

        while (true)
        {
            var response = await prompt.ReadLineAsync().ConfigureAwait(false);

            if (response is ExitApplicationKeyPress)
            {
                break;
            }

            if (response.IsSuccess)
            {
                var commandText = response.Text.Trim().ToLowerInvariant();

                // evaluate built in commands
                if (commandText == "exit")
                {
                    break;
                }
                if (commandText == "clear")
                {
                    console.Clear();
                    continue;
                }
                if (new[] { "help", "#help", "?" }.Contains(commandText))
                {
                    PrintHelp(config.KeyBindings, config.SubmitPromptDetailedKeys);
                    continue;
                }

                // evaluate results returned by special keybindings (configured in the PromptConfiguration.cs)
                if (response is KeyPressCallbackResult callbackOutput)
                {
                    console.WriteLine(Environment.NewLine + callbackOutput.Output);
                    continue;
                }

                response.CancellationToken.Register(() => Environment.Exit(1));

                // evaluate C# code and directives
                var result = await roslyn
                    .EvaluateAsync(response.Text, config.LoadScriptArgs, response.CancellationToken)
                    .ConfigureAwait(false);

                var displayDetails = config.SubmitPromptDetailedKeys.Matches(
                    response.SubmitKeyInfo
                );
                await PrintAsync(
                    roslyn,
                    console,
                    result,
                    displayDetails ? Level.FirstDetailed : Level.FirstSimple,
                    response.Text.Trim()
                );
            }
        }
    }

    public static async Task Preload(
        RoslynServices roslyn,
        IConsoleEx console,
        Configuration config
    )
    {
        ReadEvalPrintLoop.config = config;
        bool hasReferences = config.References.Count > 0;
        bool hasLoadScript = config.LoadScript is not null;
        if (!hasReferences && !hasLoadScript)
        {
            _ = roslyn.WarmUpAsync(config.LoadScriptArgs); // don't await; we don't want to block the console while warmup happens.
            return;
        }

        if (hasReferences)
        {
            console.WriteLine("Adding supplied references...");
            var loadReferenceScript = string.Join(
                "\r\n",
                config.References
                    .Select(reference => $@"#r ""{reference}""")
                    .Where(reference => !reference.Contains("Anonymously Hosted DynamicMethods"))
                    .Where(reference => !reference.Contains("-"))
            );
            var loadReferenceScriptResult = await roslyn
                .EvaluateAsync(loadReferenceScript)
                .ConfigureAwait(false);
            await PrintAsync(roslyn, console, loadReferenceScriptResult, level: Level.FirstSimple)
                .ConfigureAwait(false);
        }

        if (hasLoadScript)
        {
            console.WriteLine("Running supplied CSX file...");
            var loadScriptResult = await roslyn
                .EvaluateAsync(config.LoadScript!, config.LoadScriptArgs)
                .ConfigureAwait(false);
            await PrintAsync(roslyn, console, loadScriptResult, level: Level.FirstSimple)
                .ConfigureAwait(false);
        }
    }

    private static async Task PrintAsync(
        RoslynServices roslyn,
        IConsoleEx console,
        EvaluationResult result,
        Level level,
        string? commandText = null
    )
    {
        switch (result)
        {
            case EvaluationResult.Success ok:
                if (ok.ReturnValue.HasValue)
                {
                    var formatted = await roslyn.PrettyPrintAsync(ok.ReturnValue.Value, level);
                    if (!string.IsNullOrEmpty(commandText) && config.LogSuccess is not null)
                    {
                        config.LogSuccess(commandText, new { Result = ok.ReturnValue.Value });
                    }
                    else
                    {
                        console.Write(formatted);
                    }
                }
                console.WriteLine();
                break;
            case EvaluationResult.Error err:
                var formattedError = await roslyn.PrettyPrintAsync(err.Exception, level);
                if (config.LogError is not null)
                {
                    config.LogError(commandText, err.Exception, new { Error = err.Exception });
                }
                var panel = new Panel(formattedError.ToParagraph())
                {
                    Header = new PanelHeader(err.Exception.GetType().Name, Justify.Center),
                    BorderStyle = new Style(foreground: Color.Red),
                };
                console.WriteError(panel, formattedError.ToString());
                console.WriteLine();
                break;
            case EvaluationResult.Cancelled:
                console.WriteErrorLine(
                    AnsiColor.Yellow.GetEscapeSequence()
                        + "Operation cancelled."
                        + AnsiEscapeCodes.Reset
                );
                break;
        }
    }

    private void PrintHelp(KeyBindings keyBindings, KeyPressPatterns submitPromptDetailedKeys)
    {
        var newLineBindingName = KeyPressPatternToString(keyBindings.NewLine.DefinedPatterns ?? []);
        var submitPromptName = KeyPressPatternToString(
            (keyBindings.SubmitPrompt.DefinedPatterns ?? []).Except(
                submitPromptDetailedKeys.DefinedPatterns ?? []
            )
        );
        var submitPromptDetailedName = KeyPressPatternToString(
            submitPromptDetailedKeys.DefinedPatterns ?? []
        );

        console.WriteLine(
            FormattedStringParser.Parse(
                $"""
More details and screenshots are available at
[blue]https://github.com/waf/CSharpRepl/blob/main/README.md [/]

[underline]Evaluating Code[/]
Type C# code at the prompt and press:
  - {submitPromptName} to run it and get result printed,
  - {submitPromptDetailedName} to run it and get result printed with more details (member info, stack traces, etc.),
  - {newLineBindingName} to insert a newline (to support multiple lines of input).
If the code isn't a complete statement, pressing [green]Enter[/] will insert a newline.

[underline]Global Variables[/]
  - [green]{"ScriptGlobals"}[/]:  All global variables and services are static properties of the ScriptGlobals class. Feel free to add new ones as needed.

[underline]Exploring Code[/]
  - [green]{"F1"}[/]:  when the caret is in a type or member, open the corresponding MSDN documentation.
  - [green]{"F9"}[/]:  show the IL (intermediate language) for the current statement.
  - [green]{"F12"}[/]: open the type's source code in the browser, if the assembly supports Source Link.

[underline]Configuration Options[/]
All configuration, including theming, is done at startup via command line flags.
Run [green]--help[/] at the command line to view these options.
"""
            )
        );

        static string KeyPressPatternToString(IEnumerable<KeyPressPattern> patterns)
        {
            var values = patterns.ToList();
            return values.Count > 0
                ? string.Join(
                    " or ",
                    values.Select(pattern => $"[green]{pattern.GetStringValue()}[/]")
                )
                : "[red]<undefined>[/]";
        }
    }

    private static string Help =>
        PromptConfiguration.HasUserOptedOutFromColor
            ? @"""help"""
            : AnsiColor.Green.GetEscapeSequence() + "help" + AnsiEscapeCodes.Reset;

    private static string Exit =>
        PromptConfiguration.HasUserOptedOutFromColor
            ? @"""exit"""
            : AnsiColor.BrightRed.GetEscapeSequence() + "exit" + AnsiEscapeCodes.Reset;

    private static string Clear =>
        PromptConfiguration.HasUserOptedOutFromColor
            ? @"""clear"""
            : AnsiColor.BrightBlue.GetEscapeSequence() + "clear" + AnsiEscapeCodes.Reset;
}
