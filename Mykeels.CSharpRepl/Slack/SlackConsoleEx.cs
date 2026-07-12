// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO;
using System.Text;
using System.Threading.Channels;
using CSharpRepl.Services;
using PrettyPrompt.Consoles;
using SlackNet;
using SlackNet.WebApi;
using Spectre.Console;
using Spectre.Console.Rendering;
// System.Threading.Channels.Channel (the static factory class) collides with SlackNet.Channel.
using SystemChannel = System.Threading.Channels.Channel;

namespace Mykeels.CSharpRepl;

/// <summary>
/// <see cref="IConsoleEx"/> adapter bound to a single Slack thread. Input arrives as whole messages (written
/// into <see cref="Inbound"/> by whatever is listening to Slack, e.g. <c>SlackReplHost</c>) rather than key
/// presses, so <see cref="IsInteractive"/> is <see langword="false"/> and <c>Repl.Run</c> drives this console
/// with <see cref="MessageReadEvalPrintLoop"/> instead of <see cref="PrettyPrompt.Prompt"/>.
/// </summary>
public sealed class SlackConsoleEx : IConsoleEx, IAsyncLineConsole
{
    /// <summary>Slack's per-message character limit, minus headroom for the surrounding code-fence.</summary>
    private const int MaxMessageLength = 39_000;

    /// <summary>How much of a line to include in a log message before truncating it for readability.</summary>
    private const int LogPreviewLength = 300;

    private readonly ISlackApiClient slack;
    private readonly string channel;
    private readonly string threadTs;
    private readonly Action<string> log;
    private readonly Channel<string> inbound;
    private readonly StringBuilder buffer = new();
    private readonly IAnsiConsole ansiConsole;
    private readonly object bufferLock = new();

    public SlackConsoleEx(ISlackApiClient slack, string channel, string threadTs, Action<string>? log = null)
    {
        this.slack = slack;
        this.channel = channel;
        this.threadTs = threadTs;
        this.log = log ?? (_ => { });
        inbound = SystemChannel.CreateUnbounded<string>();
        ansiConsole = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(new StringWriter(buffer)),
                ColorSystem = ColorSystemSupport.NoColors,
            }
        );
    }

    /// <summary>
    /// Written to by whatever is listening for Slack events for this thread (e.g. <c>SlackReplHost</c>'s
    /// message handler), and read from by <see cref="ReadLineAsync"/>. Completing this writer ends the
    /// session (<see cref="ReadLineAsync"/> then returns <see langword="null"/>), e.g. for an idle timeout.
    /// </summary>
    public ChannelWriter<string> Inbound => inbound.Writer;

    public bool IsInteractive => false;

    public IConsole PrettyPromptConsole { get; } = new SlackConsole();

    public Profile Profile => ansiConsole.Profile;
    public IAnsiConsoleCursor Cursor => ansiConsole.Cursor;
    public IAnsiConsoleInput Input => ansiConsole.Input;
    public IExclusivityMode ExclusivityMode => ansiConsole.ExclusivityMode;
    public RenderPipeline Pipeline => ansiConsole.Pipeline;

    /// <summary>Nothing to clear in a chat thread.</summary>
    public void Clear(bool home) { }

    public void Write(IRenderable renderable)
    {
        lock (bufferLock)
        {
            ansiConsole.Write(renderable);
        }
    }

    public string? ReadLine() => ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        string? line;
        try
        {
            line = await inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            log($"[{channel}/{threadTs}] session closed, no more input");
            return null;
        }

        log($"[{channel}/{threadTs}] << {Preview(line)}");
        return line;
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        string text;
        lock (bufferLock)
        {
            if (buffer.Length == 0)
            {
                return;
            }
            text = buffer.ToString();
            buffer.Clear();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.Length > MaxMessageLength)
        {
            var hiddenCharacters = text.Length - MaxMessageLength;
            text = $"{text[..MaxMessageLength]}\n... (truncated, {hiddenCharacters} more characters)";
        }

        log($"[{channel}/{threadTs}] >> {Preview(text)}");

        await slack
            .Chat.PostMessage(
                new Message { Channel = channel, ThreadTs = threadTs, Text = $"```{text}```" },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static string Preview(string text) =>
        text.Length > LogPreviewLength ? $"{text[..LogPreviewLength]}... ({text.Length} chars)" : text;
}
