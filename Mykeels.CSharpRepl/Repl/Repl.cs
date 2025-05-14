using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CSharpRepl.Logging;
using CSharpRepl.Services;
using CSharpRepl.Services.Logging;
using CSharpRepl.Services.Roslyn;
using PrettyPrompt;
using CSharpRepl.Services.Roslyn.Scripting;

namespace Mykeels.CSharpRepl;

public static class Repl
{
    public static async Task<EvaluationResult> Evaluate(string commandText)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        var config = new Configuration();

        // parse command line input
        IConsoleEx console = new SystemConsoleEx();

        SetDefaultCulture(config);

        // initialize roslyn
        var logger = InitializeLogging(config.Trace);
        var roslyn = new RoslynServices(console, config, logger);
        await ReadEvalPrintLoop.Preload(roslyn, console, config);

        try
        {
            return await roslyn
                .EvaluateAsync(commandText, config.LoadScriptArgs, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new EvaluationResult.Error(exception);
        }
    }

    public static async Task<int> Run(Configuration? config = null, List<string>? commands = null, Action<RoslynServices>? onLoad = null)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        config ??= new Configuration();
        commands ??= new List<string>();
        onLoad ??= (roslyn) => { };

        // parse command line input
        IConsoleEx console = new SystemConsoleEx();
        var appStorage = CreateApplicationStorageDirectory();

        SetDefaultCulture(config);

        if (config.OutputForEarlyExit.Text is not null)
        {
            console.WriteLine(config.OutputForEarlyExit);
            return ExitCodes.Success;
        }

        // initialize roslyn
        var logger = InitializeLogging(config.Trace);
        var roslyn = new RoslynServices(console, config, logger);

        // we're being run interactively, start the prompt
        var (prompt, exitCode) = InitializePrompt(console, appStorage, roslyn, config);
        if (prompt is not null)
        {
            try
            {
                await new ReadEvalPrintLoop(console, roslyn, prompt)
                    .RunAsync(config, roslyn => {
                        onLoad?.Invoke(roslyn);
                        foreach (var command in commands)
                        {
                            roslyn
                                .EvaluateAsync(command, config.LoadScriptArgs, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    })
                    .ConfigureAwait(false);
            }
            finally
            {
                await prompt.DisposeAsync().ConfigureAwait(false);
            }
        }

        return exitCode;
    }

    /// <summary>
    /// Create application storage directory and return its path.
    /// This is where prompt history and nuget packages are stored.
    /// </summary>
    private static string CreateApplicationStorageDirectory()
    {
        var appStorage = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".csharprepl"
        );
        Directory.CreateDirectory(appStorage);
        return appStorage;
    }

    private static void SetDefaultCulture(Configuration config)
    {
        CultureInfo.DefaultThreadCurrentUICulture = config.Culture;
        // theoretically we shouldn't need to do the following, but in practice we need it in order to
        // get compiler errors emitted by CSharpScript in the right language (see https://github.com/waf/CSharpRepl/issues/312)
        CultureInfo.DefaultThreadCurrentCulture = config.Culture;
    }

    /// <summary>
    /// Initialize logging. It's off by default, unless the user passes the --trace flag.
    /// </summary>
    private static ITraceLogger InitializeLogging(bool trace) =>
        !trace
            ? new NullLogger()
            : TraceLogger.Create($"csharprepl-tracelog-{DateTime.UtcNow:yyyy-MM-dd}.txt");

    private static (Prompt? prompt, int exitCode) InitializePrompt(
        IConsoleEx console,
        string appStorage,
        RoslynServices roslyn,
        Configuration config
    )
    {
        try
        {
            var prompt = new Prompt(
                persistentHistoryFilepath: Path.Combine(appStorage, "prompt-history"),
                callbacks: new CSharpReplPromptCallbacks(console, roslyn, config),
                configuration: new PromptConfiguration(
                    keyBindings: config.KeyBindings,
                    prompt: config.Prompt,
                    completionBoxBorderFormat: config.Theme.GetCompletionBoxBorderFormat(),
                    completionItemDescriptionPaneBackground: config.Theme.GetCompletionItemDescriptionPaneBackground(),
                    selectedCompletionItemBackground: config.Theme.GetSelectedCompletionItemBackgroundColor(),
                    selectedTextBackground: config.Theme.GetSelectedTextBackground(),
                    tabSize: config.TabSize
                ),
                console: console.PrettyPromptConsole
            );
            return (prompt, ExitCodes.Success);
        }
        catch (InvalidOperationException ex)
            when (ex.Message.EndsWith("error code: 87", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Failed to initialize prompt. Please make sure that the current terminal supports ANSI escape sequences."
                    + Environment.NewLine
                    + (
                        OperatingSystem.IsWindows()
                            ? @"This requires at least Windows 10 version 1511 (build number 10586) and ""Use legacy console"" to be disabled in the Command Prompt."
                                + Environment.NewLine
                            : string.Empty
                    )
            );
            return (null, ExitCodes.ErrorAnsiEscapeSequencesNotSupported);
        }
        catch (InvalidOperationException ex)
            when (ex.Message.EndsWith("error code: 6", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Failed to initialize prompt. Invalid output mode -- is output redirected?"
            );
            return (null, ExitCodes.ErrorInvalidConsoleHandle);
        }
    }
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int ErrorParseArguments = 1;
    public const int ErrorAnsiEscapeSequencesNotSupported = 2;
    public const int ErrorInvalidConsoleHandle = 3;
    public const int ErrorCancelled = 3;
}
