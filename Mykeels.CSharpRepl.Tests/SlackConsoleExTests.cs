using CSharpRepl.Services;
using NSubstitute;
using SlackNet;
using SlackNet.WebApi;

namespace Mykeels.CSharpRepl.Tests;

public class SlackConsoleExTests
{
    private ISlackApiClient slack = null!;
    private IChatApi chat = null!;
    private IFilesApi files = null!;
    private SlackConsoleEx console = null!;
    private List<string> logLines = null!;

    // IConsoleEx.WriteLine etc. are default interface members, so they're only visible through the
    // interface, not through a variable typed as the concrete SlackConsoleEx.
    private IConsoleEx AsConsoleEx => console;

    [SetUp]
    public void Setup()
    {
        slack = Substitute.For<ISlackApiClient>();
        chat = Substitute.For<IChatApi>();
        slack.Chat.Returns(chat);
        chat.PostMessage(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PostMessageResponse()));
        files = Substitute.For<IFilesApi>();
        slack.Files.Returns(files);

        logLines = [];
        console = new SlackConsoleEx(slack, channel: "C123", threadTs: "111.222", log: logLines.Add);
    }

    [Test]
    public async Task FlushAsync_PostsBufferedWriteLineOutputToTheThread()
    {
        AsConsoleEx.WriteLine("hello from the repl");

        await console.FlushAsync(CancellationToken.None);

        await chat
            .Received(1)
            .PostMessage(
                Arg.Is<Message>(m =>
                    m.Channel == "C123"
                    && m.ThreadTs == "111.222"
                    && m.Text != null
                    && m.Text.Contains("hello from the repl")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task FlushAsync_WithNoBufferedOutput_DoesNotPostAMessage()
    {
        await console.FlushAsync(CancellationToken.None);

        await chat.DidNotReceive().PostMessage(Arg.Any<Message>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FlushAsync_ClearsTheBufferSoOutputIsNotPostedTwice()
    {
        AsConsoleEx.WriteLine("first turn");
        await console.FlushAsync(CancellationToken.None);

        await console.FlushAsync(CancellationToken.None);

        await chat.Received(1).PostMessage(Arg.Any<Message>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FlushAsync_TruncatesOutputLongerThanSlacksMessageLimit()
    {
        AsConsoleEx.WriteLine(new string('x', 50_000));

        await console.FlushAsync(CancellationToken.None);

        await chat
            .Received(1)
            .PostMessage(
                Arg.Is<Message>(m => m.Text != null && m.Text.Contains("truncated") && m.Text.Length < 50_000),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task ReadLineAsync_ReturnsMessagesWrittenToInbound()
    {
        await console.Inbound.WriteAsync("1 + 1");

        var line = await console.ReadLineAsync(CancellationToken.None);

        Assert.That(line, Is.EqualTo("1 + 1"));
    }

    [Test]
    public async Task ReadLineAsync_ReturnsNullOnceInboundIsCompleted()
    {
        console.Inbound.Complete();

        var line = await console.ReadLineAsync(CancellationToken.None);

        Assert.That(line, Is.Null);
    }

    [Test]
    public void IsInteractive_IsFalse()
    {
        Assert.That(console.IsInteractive, Is.False);
    }

    [Test]
    public async Task FlushAsync_LogsTheOutgoingMessage()
    {
        AsConsoleEx.WriteLine("hello from the repl");

        await console.FlushAsync(CancellationToken.None);

        Assert.That(logLines, Has.Some.Contain("hello from the repl"));
    }

    [Test]
    public async Task ReadLineAsync_LogsTheIncomingMessage()
    {
        await console.Inbound.WriteAsync("1 + 1");

        await console.ReadLineAsync(CancellationToken.None);

        Assert.That(logLines, Has.Some.Contain("1 + 1"));
    }

    [Test]
    public async Task FlushAsync_WithAtMost16Lines_PostsInlineInsteadOfUploading()
    {
        // WriteLine appends its own trailing newline, so 15 joined lines + that = 16 total lines buffered.
        AsConsoleEx.WriteLine(string.Join('\n', Enumerable.Repeat("line", 15)));

        await console.FlushAsync(CancellationToken.None);

        await chat.Received(1).PostMessage(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        await files.DidNotReceive().Upload(Arg.Any<FileUpload>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FlushAsync_WithMoreThan16Lines_UploadsAsASnippetInsteadOfPosting()
    {
        // 16 joined lines + WriteLine's own trailing newline = 17 total lines buffered.
        AsConsoleEx.WriteLine(string.Join('\n', Enumerable.Repeat("line", 16)));

        await console.FlushAsync(CancellationToken.None);

        await chat.DidNotReceive().PostMessage(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        await files
            .Received(1)
            .Upload(
                Arg.Is<FileUpload>(f => f.SnippetType == "json"),
                channelId: "C123",
                threadTs: "111.222",
                initialComment: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }
}
