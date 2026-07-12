// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;

namespace Mykeels.CSharpRepl;

/// <summary>
/// Optional capability for <see cref="CSharpRepl.Services.IConsoleEx"/> implementations that back onto a
/// message-based transport (e.g. Slack). <see cref="MessageReadEvalPrintLoop"/> prefers this over the
/// synchronous <c>IConsoleEx.ReadLine</c> so that waiting for the next message doesn't have to block a thread.
/// </summary>
internal interface IAsyncLineConsole
{
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Flush any output buffered since the last flush (e.g. as a single posted Slack message). No-op for
    /// consoles that write output immediately.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken);
}
