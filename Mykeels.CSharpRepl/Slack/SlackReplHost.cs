// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Net;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using SlackNet;
using SlackNet.Events;
using SlackNet.Interaction;
using SlackNet.SocketMode;
using SlackNet.WebApi;

namespace Mykeels.CSharpRepl;

/// <summary>
/// Owns the Slack connection for the whole <c>/new-csharp-repl</c> feature: one <see cref="SlackReplHost"/>
/// can have many sessions (threads) open at once, each running its own <c>Repl.Run</c> against a
/// <see cref="SlackConsoleEx"/> bound to that thread. See
/// <c>ideas/2026-07-12-add-slack-integration.md</c> for the overall design.
/// </summary>
public sealed class SlackReplHost
{
    private readonly SlackReplOptions options;
    private readonly SlackAuthorizer authorizer;
    private readonly ISlackApiClient api;
    private readonly Configuration? config;
    private readonly List<string> commands;
    private readonly Action<RoslynServices>? onLoad;
    private readonly Action<string> log;
    private readonly ConcurrentDictionary<(string ChannelId, string ThreadTs), SlackReplSession> sessions = new();
    private string? botUserId;

    /// <summary>
    /// Exposed internally so tests can drive <see cref="SlackReplHost"/> against a fake <see cref="ISlackApiClient"/>
    /// without a real Socket Mode connection. <see cref="Run"/> is the public entry point.
    /// </summary>
    internal SlackReplHost(
        SlackReplOptions options,
        ISlackApiClient api,
        Configuration? config = null,
        List<string>? commands = null,
        Action<RoslynServices>? onLoad = null
    )
    {
        // Fails closed here if AllowedUserIds/AllowedChannelIds are both unset — see SlackAuthorizer.
        authorizer = new SlackAuthorizer(options);
        this.options = options;
        this.api = api;
        this.config = config;
        this.commands = commands ?? [];
        this.onLoad = onLoad;
        log = options.Log;
    }

    /// <summary>
    /// Connects to Slack over Socket Mode and serves <c>/new-csharp-repl</c> sessions until
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public static async Task Run(
        SlackReplOptions options,
        Configuration? config = null,
        List<string>? commands = null,
        Action<RoslynServices>? onLoad = null,
        CancellationToken cancellationToken = default
    )
    {
        var builder = new SlackServiceBuilder().UseApiToken(options.BotToken).UseAppLevelToken(options.AppToken);

        // Resolved before registering handlers: GetApiClient() only depends on the tokens above, not on the
        // handler registrations below, and this instance is what the host uses to call back into Slack.
        var api = builder.GetApiClient();
        var host = new SlackReplHost(options, api, config, commands, onLoad);

        builder
            .RegisterSlashCommandHandler(options.SlashCommand, new SlashCommandHandler(host))
            .RegisterEventHandler<MessageEvent>(new MessageEventHandler(host));

        await host.ConnectAsync(builder.GetSocketModeClient(), cancellationToken).ConfigureAwait(false);
    }

    private async Task ConnectAsync(ISlackSocketModeClient socketModeClient, CancellationToken cancellationToken)
    {
        var identity = await api.Auth.Test(cancellationToken).ConfigureAwait(false);
        botUserId = identity.UserId;
        log($"connected as {identity.User} ({botUserId}), listening for {options.SlashCommand}");

        if (options.IdleTimeout is { } idleTimeout)
        {
            // Deliberately not awaited: runs alongside the socket connection for the lifetime of the host.
            _ = RunIdleTimeoutLoopAsync(idleTimeout, cancellationToken);
        }

        await socketModeClient.Connect(new SocketModeConnectionOptions(), cancellationToken).ConfigureAwait(false);

        // Connect() establishes the connection and returns once it's up — it doesn't block for the
        // connection's lifetime, and the socket keeps running in the background via SlackNet's own
        // handling. Block here until cancelled so the host process doesn't just exit right after connecting.
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            log("cancellation requested, disconnecting");
            socketModeClient.Disconnect();
        }
    }

    /// <summary>Test seam — production code has no need to inspect open sessions from the outside.</summary>
    internal SlackReplSession? GetSession(string channelId, string threadTs) =>
        sessions.TryGetValue((channelId, threadTs), out var session) ? session : null;

    /// <summary>Test seam — see <see cref="GetSession"/>.</summary>
    internal int SessionCount => sessions.Count;

    internal Task<SlashCommandResponse> HandleSlashCommand(SlashCommand command)
    {
        log($"{options.SlashCommand} from user={command.UserId} channel={command.ChannelId}");

        if (!authorizer.CanStartSession(command.UserId, command.ChannelId))
        {
            log($"denied: user={command.UserId} channel={command.ChannelId} is not authorized to start a session");
            return Task.FromResult(
                new SlashCommandResponse
                {
                    ResponseType = ResponseType.Ephemeral,
                    Message = new Message { Text = "You're not authorized to start a C# REPL session here." },
                }
            );
        }

        // Fire-and-forget: Slack expects an ack within 3 seconds, so the actual thread-starting PostMessage and
        // session creation happen after we've already returned the ack below.
        _ = StartSessionAsync(command.ChannelId, command.UserId);

        return Task.FromResult(
            new SlashCommandResponse
            {
                ResponseType = ResponseType.Ephemeral,
                Message = new Message { Text = "Starting your C# REPL session..." },
            }
        );
    }

    private async Task StartSessionAsync(string channelId, string userId)
    {
        // This runs fire-and-forget from HandleSlashCommand (see the comment there), so any exception here
        // would otherwise vanish silently instead of surfacing anywhere — log it, and let the user know in
        // Slack too, so a bad channel/scope/token doesn't look like the bot just did nothing.
        try
        {
            var started = await api
                .Chat.PostMessage(
                    new Message
                    {
                        Channel = channelId,
                        Text =
                            $":computer: New C# REPL session started by <@{userId}>. Reply in this thread to evaluate code; send `exit` to end the session.",
                    },
                    CancellationToken.None
                )
                .ConfigureAwait(false);

            var threadTs = started.Ts;
            var consoleEx = new SlackConsoleEx(api, channelId, threadTs, log);
            var key = (channelId, threadTs);
            log($"session started: channel={channelId} thread={threadTs} owner={userId}");

            var replTask = Task.Factory
                .StartNew(
                    () => Repl.Run(config, commands, onLoad, consoleEx),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                )
                .Unwrap();

            sessions[key] = new SlackReplSession
            {
                ChannelId = channelId,
                ThreadTs = threadTs,
                OwnerUserId = userId,
                Console = consoleEx,
                ReplTask = replTask,
            };

            // Deliberately not awaited: runs in the background once the session's Repl.Run task finishes.
            _ = OnSessionEndedAsync(key, replTask);
        }
        catch (Exception exception)
        {
            log($"failed to start session: channel={channelId} owner={userId}: {exception}");
            try
            {
                await api
                    .Chat.PostEphemeral(
                        userId,
                        new Message { Channel = channelId, Text = $":warning: Failed to start REPL session: {exception.Message}" },
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
            catch
            {
                // Best-effort notice — if this also fails (e.g. same permission problem), the log line above is
                // the fallback, not a second silent failure.
            }
        }
    }

    private async Task OnSessionEndedAsync((string ChannelId, string ThreadTs) key, Task replTask)
    {
        string closingText;
        try
        {
            await replTask.ConfigureAwait(false);
            closingText = ":white_check_mark: Session ended.";
        }
        catch (Exception exception)
        {
            closingText = $":warning: Session ended unexpectedly: {exception.Message}";
        }
        finally
        {
            sessions.TryRemove(key, out _);
        }

        log($"session ended: channel={key.ChannelId} thread={key.ThreadTs} ({closingText})");

        await api
            .Chat.PostMessage(
                new Message
                {
                    Channel = key.ChannelId,
                    ThreadTs = key.ThreadTs,
                    Text = closingText,
                },
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    internal Task HandleMessageEvent(MessageEvent message)
    {
        // BotId is set on messages posted by any bot, including this one; User == botUserId is a fallback for
        // clients where that's not populated. Either way, don't feed the REPL's own output back into itself.
        if (message.User is null || message.User == botUserId || message.BotId is not null)
        {
            log($"ignored: user={message.User} channel={message.Channel} thread={message.ThreadTs} botId={message.BotId}");
            return Task.CompletedTask;
        }

        var threadTs = message.ThreadTs ?? message.Ts;
        if (!sessions.TryGetValue((message.Channel, threadTs), out var session))
        {
            log($"ignored: user={message.User} channel={message.Channel} thread={threadTs} not found in sessions");
            return Task.CompletedTask;
        }

        if (!authorizer.CanReply(message.User, message.Channel, session.OwnerUserId))
        {
            log(
                $"denied: user={message.User} channel={message.Channel} thread={threadTs} is not authorized to reply in this session"
            );
            return PostUnauthorizedReplyNoticeAsync(message.Channel, threadTs);
        }
        // Slack HTML-escapes &, <, and > in message text (so e.g. `List<string>` arrives as
        // `List&lt;string&gt;`) — decode before treating it as C# source.
        var text = WebUtility.HtmlDecode(message.Text) ?? string.Empty;
        log($"received: user={message.User} channel={message.Channel} thread={threadTs} text={text}");

        session.Touch();
        session.Console.Inbound.TryWrite(text);
        return Task.CompletedTask;
    }

    private async Task PostUnauthorizedReplyNoticeAsync(string channelId, string threadTs)
    {
        await api
            .Chat.PostMessage(
                new Message
                {
                    Channel = channelId,
                    ThreadTs = threadTs,
                    Text = "Only the user who started this session can send commands here.",
                },
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    private async Task RunIdleTimeoutLoopAsync(TimeSpan idleTimeout, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = DateTime.UtcNow;
            foreach (var session in sessions.Values)
            {
                if (now - session.LastActivityUtc > idleTimeout)
                {
                    log($"idle timeout: channel={session.ChannelId} thread={session.ThreadTs}");
                    session.Console.Inbound.Complete();
                }
            }
        }
    }

    private sealed class SlashCommandHandler(SlackReplHost host) : ISlashCommandHandler
    {
        public Task<SlashCommandResponse> Handle(SlashCommand command) => host.HandleSlashCommand(command);
    }

    private sealed class MessageEventHandler(SlackReplHost host) : IEventHandler<MessageEvent>
    {
        public Task Handle(MessageEvent message) => host.HandleMessageEvent(message);
    }
}
