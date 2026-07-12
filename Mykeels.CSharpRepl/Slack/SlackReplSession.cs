// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;

namespace Mykeels.CSharpRepl;

/// <summary>A single open Slack-hosted REPL session, tracked by <see cref="SlackReplHost"/>.</summary>
internal sealed class SlackReplSession
{
    private long lastActivityUtcTicks = DateTime.UtcNow.Ticks;

    public required string ChannelId { get; init; }
    public required string ThreadTs { get; init; }
    public required string OwnerUserId { get; init; }
    public required SlackConsoleEx Console { get; init; }
    public required Task ReplTask { get; init; }

    public DateTime LastActivityUtc => new(Interlocked.Read(ref lastActivityUtcTicks));

    public void Touch() => Interlocked.Exchange(ref lastActivityUtcTicks, DateTime.UtcNow.Ticks);
}
