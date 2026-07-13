// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;
using PrettyPrompt.Consoles;

namespace Mykeels.CSharpRepl;

/// <summary>
/// Minimal stub for <see cref="IConsole"/>, PrettyPrompt's terminal abstraction. <see cref="SlackConsoleEx"/>
/// is required to expose one (<c>IConsoleEx.PrettyPromptConsole</c>), but since Slack sessions never construct
/// a real <see cref="PrettyPrompt.Prompt"/> (see <see cref="MessageReadEvalPrintLoop"/>), nothing here should
/// ever actually be called. <see cref="IsErrorRedirected"/> is the one member the shared
/// <see cref="CSharpRepl.Services.IConsoleEx"/> default methods read directly, so it answers for real; everything
/// else throws to surface a bug loudly if this assumption ever stops holding.
/// </summary>
internal sealed class SlackConsole : IConsole
{
    public bool IsErrorRedirected => false;
    public bool IsInputRedirected => throw Unsupported();
    public bool IsOutputRedirected => throw Unsupported();
    public bool KeyAvailable => throw Unsupported();
    public int CursorTop => throw Unsupported();
    public int BufferWidth => throw Unsupported();
    public int WindowHeight => throw Unsupported();
    public int WindowWidth => throw Unsupported();
    public int WindowTop => throw Unsupported();
    public bool CaptureControlC
    {
        get => throw Unsupported();
        set => throw Unsupported();
    }
    public string Title
    {
        get => throw Unsupported();
        set => throw Unsupported();
    }

    public void HideCursor() => throw Unsupported();
    public void ShowCursor() => throw Unsupported();
    public void Write(string? value) => throw Unsupported();
    public void Write(ReadOnlySpan<char> value) => throw Unsupported();
    public void WriteError(string? value) => throw Unsupported();
    public void WriteError(ReadOnlySpan<char> value) => throw Unsupported();
    public void WriteLine(string? value = null) => throw Unsupported();
    public void WriteLine(ReadOnlySpan<char> value) => throw Unsupported();
    public void WriteErrorLine(string? value) => throw Unsupported();
    public void WriteErrorLine(ReadOnlySpan<char> value) => throw Unsupported();
    public void Clear() => throw Unsupported();
    public ConsoleKeyInfo ReadKey(bool intercept) => throw Unsupported();
    public void InitVirtualTerminalProcessing() => throw Unsupported();

    public event ConsoleCancelEventHandler? CancelKeyPress
    {
        add { }
        remove { }
    }

    private static NotSupportedException Unsupported([CallerMemberName] string member = "") =>
        new(
            $"{member} is not supported by {nameof(SlackConsole)} — Slack sessions bypass PrettyPrompt.Prompt entirely."
        );
}
