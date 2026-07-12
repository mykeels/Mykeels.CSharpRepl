using CSharpRepl.Services;
using NSubstitute;
using SlackNet;
using SlackNet.WebApi;

namespace Mykeels.CSharpRepl.Tests;

public class SlackConsoleExTests
{
    private ISlackApiClient slack = null!;
    private IChatApi chat = null!;
    private SlackConsoleEx console = null!;

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

        console = new SlackConsoleEx(slack, channel: "C123", threadTs: "111.222");
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
}
