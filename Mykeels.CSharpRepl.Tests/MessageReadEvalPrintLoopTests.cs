using System.Collections.Concurrent;
using System.Text;
using CSharpRepl.Services;
using Mykeels.CSharpRepl.Sample;
using PrettyPrompt.Consoles;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Mykeels.CSharpRepl.Tests;

public class MessageReadEvalPrintLoopTests
{
    [Test]
    public async Task Run_EvaluatesMessagesAndEndsOnExit()
    {
        var console = new FakeConsoleEx();
        console.Enqueue("1 + 1");
        console.Enqueue("exit");

        var exitCode = await Repl.Run(
            commands:
            [
                "using static Mykeels.CSharpRepl.Sample.ScriptGlobals;",
                "using Mykeels.CSharpRepl.Sample;",
            ],
            console: console
        );

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(console.Output, Does.Contain("2"));
    }

    [Test]
    public async Task Run_PrintsSuccessfulResultsAsIndentedJson()
    {
        var console = new FakeConsoleEx();
        console.Enqueue("new { A = 1, B = \"hi\" }");
        console.Enqueue("exit");

        await Repl.Run(console: console);

        Assert.That(console.Output, Does.Contain("\"A\": 1"));
        Assert.That(console.Output, Does.Contain("{" + Environment.NewLine));
    }

    [Test]
    public async Task Run_HelpCommand_ListsScriptGlobalsMembersWithTypeInfo_WhenBroughtIntoScopeViaUsingStatic()
    {
        var console = new FakeConsoleEx();
        console.Enqueue("using static Mykeels.CSharpRepl.Sample.ScriptGlobals;");
        console.Enqueue("help");
        console.Enqueue("exit");

        await Repl.Run(console: console);

        Assert.That(console.Output, Does.Contain(typeof(ScriptGlobals).FullName!));
        Assert.That(console.Output, Does.Contain("void Print(object obj)"));
    }

    [Test]
    public async Task Run_HelpCommand_OmitsMembersSection_WhenNoScriptGlobalsTypeInScope()
    {
        var console = new FakeConsoleEx();
        console.Enqueue("help");
        console.Enqueue("exit");

        await Repl.Run(console: console);

        Assert.That(console.Output, Does.Not.Contain("Available members"));
    }

    [Test]
    public async Task Run_HelpCommand_IgnoresUsingStaticTypesNotFollowingTheScriptGlobalsConvention()
    {
        var console = new FakeConsoleEx();
        console.Enqueue("using static System.Math;");
        console.Enqueue("help");
        console.Enqueue("exit");

        await Repl.Run(console: console);

        Assert.That(console.Output, Does.Not.Contain("Available members"));
    }
}

file sealed class FakeConsoleEx : IConsoleEx, IAsyncLineConsole
{
    private readonly ConcurrentQueue<string> inbound = new();
    private readonly StringBuilder buffer = new();
    private readonly IAnsiConsole ansiConsole;

    public FakeConsoleEx()
    {
        ansiConsole = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(new System.IO.StringWriter(buffer)),
                ColorSystem = ColorSystemSupport.NoColors,
            }
        );
    }

    public string Output => buffer.ToString();

    public bool IsInteractive => false;

    public IConsole PrettyPromptConsole { get; } = new FakePrettyPromptConsole();

    public Profile Profile => ansiConsole.Profile;
    public IAnsiConsoleCursor Cursor => ansiConsole.Cursor;
    public IAnsiConsoleInput Input => ansiConsole.Input;
    public IExclusivityMode ExclusivityMode => ansiConsole.ExclusivityMode;
    public RenderPipeline Pipeline => ansiConsole.Pipeline;

    public void Clear(bool home) { }

    public void Write(IRenderable renderable) => ansiConsole.Write(renderable);

    public string? ReadLine() => ReadLineAsync(default).GetAwaiter().GetResult();

    public void Enqueue(string message) => inbound.Enqueue(message);

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(inbound.TryDequeue(out var message) ? message : "exit");
    }

    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

file sealed class FakePrettyPromptConsole : IConsole
{
    public bool IsErrorRedirected => false;
    public bool IsInputRedirected => false;
    public bool IsOutputRedirected => false;
    public bool KeyAvailable => false;
    public int CursorTop => 0;
    public int BufferWidth => 80;
    public int WindowHeight => 24;
    public int WindowWidth => 80;
    public int WindowTop => 0;
    public bool CaptureControlC { get => false; set { } }
    public string Title { get => string.Empty; set { } }

    public void HideCursor() { }
    public void ShowCursor() { }
    public void Write(string? value) { }
    public void Write(ReadOnlySpan<char> value) { }
    public void WriteError(string? value) { }
    public void WriteError(ReadOnlySpan<char> value) { }
    public void WriteLine(string? value = null) { }
    public void WriteLine(ReadOnlySpan<char> value) { }
    public void WriteErrorLine(string? value) { }
    public void WriteErrorLine(ReadOnlySpan<char> value) { }
    public void Clear() { }
    public ConsoleKeyInfo ReadKey(bool intercept) => default;
    public void InitVirtualTerminalProcessing() { }

    public event ConsoleCancelEventHandler? CancelKeyPress
    {
        add { }
        remove { }
    }
}
