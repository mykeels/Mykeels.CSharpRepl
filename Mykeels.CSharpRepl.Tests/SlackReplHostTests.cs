using CSharpRepl.Services;
using NSubstitute;
using SlackNet;
using SlackNet.Events;
using SlackNet.Interaction;
using SlackNet.WebApi;

namespace Mykeels.CSharpRepl.Tests;

public class SlackReplHostTests
{
    private ISlackApiClient slack = null!;
    private IChatApi chat = null!;

    [SetUp]
    public void Setup()
    {
        slack = Substitute.For<ISlackApiClient>();
        chat = Substitute.For<IChatApi>();
        slack.Chat.Returns(chat);
        chat.PostMessage(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PostMessageResponse { Ts = "100.001" }));
    }

    private SlackReplHost CreateHost(SlackReplOptions options) => new(options, slack, config: new Configuration());

    private static SlackReplOptions OptionsAllowing(string userId) =>
        new()
        {
            BotToken = "xoxb-test",
            AppToken = "xapp-test",
            AllowedUserIds = [userId],
        };

    private static async Task<SlackReplSession> WaitForSessionAsync(SlackReplHost host, string channelId, string threadTs)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (host.GetSession(channelId, threadTs) is { } session)
            {
                return session;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("Session was not created in time.");
    }

    [Test]
    public async Task HandleSlashCommand_WhenAuthorized_PostsThreadStarterAndCreatesASession()
    {
        var host = CreateHost(OptionsAllowing("U1"));

        var response = await host.HandleSlashCommand(
            new SlashCommand
            {
                UserId = "U1",
                ChannelId = "C1",
            }
        );

        Assert.That(response.ResponseType, Is.EqualTo(ResponseType.Ephemeral));
        var session = await WaitForSessionAsync(host, "C1", "100.001");
        Assert.That(session.OwnerUserId, Is.EqualTo("U1"));
        await chat.Received(1)
            .PostMessage(Arg.Is<Message>(m => m.Channel == "C1" && m.ThreadTs == null), Arg.Any<CancellationToken>());

        await session.Console.Inbound.WriteAsync("exit");
        await session.ReplTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task HandleSlashCommand_WhenNotAuthorized_RepliesEphemerallyWithoutStartingASession()
    {
        var host = CreateHost(OptionsAllowing("U1"));

        var response = await host.HandleSlashCommand(
            new SlashCommand
            {
                UserId = "U-not-allowed",
                ChannelId = "C1",
            }
        );

        Assert.That(response.ResponseType, Is.EqualTo(ResponseType.Ephemeral));
        Assert.That(response.Message.Text, Does.Contain("not authorized"));
        Assert.That(host.SessionCount, Is.EqualTo(0));
        await chat.DidNotReceive().PostMessage(Arg.Any<Message>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleMessageEvent_FromTheBotItself_IsIgnored()
    {
        var host = CreateHost(OptionsAllowing("U1"));

        await host.HandleMessageEvent(
            new MessageEvent
            {
                Channel = "C1",
                Ts = "1",
                ThreadTs = "100.001",
                User = "U1",
                BotId = "B1",
                Text = "should not be evaluated",
            }
        );

        await chat.DidNotReceive().PostMessage(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        Assert.That(host.SessionCount, Is.EqualTo(0));
    }

    [Test]
    public async Task HandleMessageEvent_ForTrackedSessionFromOwner_WritesTheMessageToTheSessionsConsole()
    {
        var host = CreateHost(OptionsAllowing("U1"));
        await host.HandleSlashCommand(
            new SlashCommand
            {
                UserId = "U1",
                ChannelId = "C1",
            }
        );
        var session = await WaitForSessionAsync(host, "C1", "100.001");

        await host.HandleMessageEvent(
            new MessageEvent
            {
                Channel = "C1",
                Ts = "200",
                ThreadTs = "100.001",
                User = "U1",
                Text = "1 + 1",
            }
        );

        var line = await session.Console.ReadLineAsync(CancellationToken.None);
        Assert.That(line, Is.EqualTo("1 + 1"));

        await session.Console.Inbound.WriteAsync("exit");
        await session.ReplTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task HandleMessageEvent_ForTrackedSessionFromNonOwner_IsIgnoredAndPostsANotice()
    {
        var host = CreateHost(OptionsAllowing("U1")); // RestrictRepliesToSessionOwner defaults to true
        await host.HandleSlashCommand(
            new SlashCommand
            {
                UserId = "U1",
                ChannelId = "C1",
            }
        );
        var session = await WaitForSessionAsync(host, "C1", "100.001");
        chat.ClearReceivedCalls();

        await host.HandleMessageEvent(
            new MessageEvent
            {
                Channel = "C1",
                Ts = "200",
                ThreadTs = "100.001",
                User = "U-someone-else",
                Text = "1 + 1",
            }
        );

        await chat.Received(1)
            .PostMessage(
                Arg.Is<Message>(m =>
                    m.ThreadTs == "100.001"
                    && m.Text != null
                    && m.Text.Contains("Only the user who started this session")
                ),
                Arg.Any<CancellationToken>()
            );

        await session.Console.Inbound.WriteAsync("exit");
        await session.ReplTask.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
