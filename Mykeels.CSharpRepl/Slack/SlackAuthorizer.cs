// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Mykeels.CSharpRepl;

/// <summary>
/// Pure authorization decisions for <see cref="SlackReplHost"/>, kept separate from the Slack transport so it's
/// testable without a socket connection. Fails closed at construction: see <see cref="SlackReplOptions.AllowedUserIds"/>.
/// </summary>
internal sealed class SlackAuthorizer
{
    private readonly SlackReplOptions options;

    public SlackAuthorizer(SlackReplOptions options)
    {
        var hasUserAllowlist = options.AllowedUserIds is { Count: > 0 };
        var hasChannelAllowlist = options.AllowedChannelIds is { Count: > 0 };
        if (!hasUserAllowlist && !hasChannelAllowlist)
        {
            throw new InvalidOperationException(
                $"{nameof(SlackReplOptions)}.{nameof(SlackReplOptions.AllowedUserIds)} or "
                    + $"{nameof(SlackReplOptions.AllowedChannelIds)} must be configured before starting "
                    + $"{nameof(SlackReplHost)} — otherwise anyone able to run the slash command gets a REPL "
                    + "with the same code-execution privileges as the host process. If you really want that, "
                    + $"set {nameof(SlackReplOptions.AllowedChannelIds)} explicitly (e.g. to every channel the "
                    + "bot is invited to) rather than leaving both unset."
            );
        }

        this.options = options;
    }

    /// <summary>Whether <paramref name="userId"/> may run the slash command in <paramref name="channelId"/>.</summary>
    public bool CanStartSession(string userId, string channelId) => IsAuthorized(userId, channelId);

    /// <summary>
    /// Whether <paramref name="userId"/> may submit code into an existing session in <paramref name="channelId"/>
    /// owned by <paramref name="sessionOwnerUserId"/>. Re-checked on every message (not just at session start)
    /// so revoking access takes effect immediately, and — by default — so only the session's owner can drive it.
    /// </summary>
    public bool CanReply(string userId, string channelId, string sessionOwnerUserId)
    {
        if (options.RestrictRepliesToSessionOwner && userId != sessionOwnerUserId)
        {
            return false;
        }

        return IsAuthorized(userId, channelId);
    }

    private bool IsAuthorized(string userId, string channelId) =>
        IsAllowedUser(userId) && IsAllowedChannel(channelId) && IsAuthorizedByCallback(userId, channelId);

    private bool IsAllowedUser(string userId) =>
        options.AllowedUserIds is not { Count: > 0 } || options.AllowedUserIds.Contains(userId);

    private bool IsAllowedChannel(string channelId) =>
        options.AllowedChannelIds is not { Count: > 0 } || options.AllowedChannelIds.Contains(channelId);

    private bool IsAuthorizedByCallback(string userId, string channelId) =>
        options.IsAuthorized is null || options.IsAuthorized(new SlackAuthorizationContext(userId, channelId));
}
