// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Mykeels.CSharpRepl;

/// <summary>
/// Configuration for <see cref="SlackReplHost"/>. See the "Auth / allowlisting" write-up in
/// <c>ideas/2026-07-12-add-slack-integration.md</c> for the reasoning behind the fail-closed defaults.
/// </summary>
public sealed class SlackReplOptions
{
    /// <summary>Bot token (<c>xoxb-...</c>), needs the <c>chat:write</c> and <c>commands</c> scopes.</summary>
    public required string BotToken { get; init; }

    /// <summary>App-level token (<c>xapp-...</c>), needs the <c>connections:write</c> scope for Socket Mode.</summary>
    public required string AppToken { get; init; }

    /// <summary>The slash command that starts a new session. Must match what's registered in the Slack app.</summary>
    public string SlashCommand { get; init; } = "/mykeels-csharp-repl";

    /// <summary>
    /// Slack user IDs allowed to start and drive sessions. <see langword="null"/>/empty means "don't restrict by
    /// user" — <b>not</b> "allow no one". At least one of <see cref="AllowedUserIds"/> or
    /// <see cref="AllowedChannelIds"/> must be set, or <see cref="SlackReplHost"/> refuses to start: anyone able
    /// to run the slash command would otherwise get a REPL with the same code-execution privileges as the host
    /// process, and that has to be opted into explicitly rather than fallen into.
    /// </summary>
    public HashSet<string>? AllowedUserIds { get; init; }

    /// <summary>Channels where the slash command may be used. See <see cref="AllowedUserIds"/> for the fail-closed rule.</summary>
    public HashSet<string>? AllowedChannelIds { get; init; }

    /// <summary>
    /// When <see langword="true"/> (the default), only the user who ran the slash command can send messages
    /// into their own session's thread — other replies in the thread are ignored (with a short notice) rather
    /// than being evaluated.
    /// </summary>
    public bool RestrictRepliesToSessionOwner { get; init; } = true;

    /// <summary>
    /// Optional additional check, e.g. against an external ACL service. Evaluated in addition to (AND'd with)
    /// <see cref="AllowedUserIds"/>/<see cref="AllowedChannelIds"/>, not instead of them.
    /// </summary>
    public Func<SlackAuthorizationContext, bool>? IsAuthorized { get; init; }

    /// <summary>
    /// If set, a session thread that receives no messages for this long is closed automatically (checked on a
    /// 30-second tick), so an abandoned thread doesn't hold a <c>RoslynServices</c> (and its loaded assemblies)
    /// alive forever. <see langword="null"/> (the default) means sessions never time out on their own.
    /// </summary>
    public TimeSpan? IdleTimeout { get; init; }

    /// <summary>
    /// Called with a line of text for every significant event as sessions progress: slash commands received,
    /// authorization decisions, messages routed into a session, output posted back to Slack, and session
    /// start/end. Defaults to writing to the console; pass <c>_ => { }</c> to silence it, or redirect it to
    /// your own logging.
    /// </summary>
    public Action<string> Log { get; init; } = message => Console.WriteLine($"[SlackRepl] {message}");
}

public readonly record struct SlackAuthorizationContext(string UserId, string ChannelId);
