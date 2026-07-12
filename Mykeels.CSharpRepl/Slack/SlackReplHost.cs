// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
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

        if (options.IdleTimeout is { } idleTimeout)
        {
            // Deliberately not awaited: runs alongside the socket connection for the lifetime of the host.
            _ = RunIdleTimeoutLoopAsync(idleTimeout, cancellationToken);
        }

        await socketModeClient.Connect(new SocketModeConnectionOptions(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Test seam — production code has no need to inspect open sessions from the outside.</summary>
    internal SlackReplSession? GetSession(string channelId, string threadTs) =>
        sessions.TryGetValue((channelId, threadTs), out var session) ? session : null;

    /// <summary>Test seam — see <see cref="GetSession"/>.</summary>
    internal int SessionCount => sessions.Count;

    internal Task<SlashCommandResponse> HandleSlashCommand(SlashCommand command)
    {
        if (!authorizer.CanStartSession(command.UserId, command.ChannelId))
        {
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
        var consoleEx = new SlackConsoleEx(api, channelId, threadTs);
        var key = (channelId, threadTs);

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
            return Task.CompletedTask;
        }

        var threadTs = message.ThreadTs ?? message.Ts;
        if (!sessions.TryGetValue((message.Channel, threadTs), out var session))
        {
            return Task.CompletedTask;
        }

        if (!authorizer.CanReply(message.User, message.Channel, session.OwnerUserId))
        {
            return PostUnauthorizedReplyNoticeAsync(message.Channel, threadTs);
        }

        session.Touch();
        session.Console.Inbound.TryWrite(message.Text ?? string.Empty);
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
