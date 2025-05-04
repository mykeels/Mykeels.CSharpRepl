using System;
using System.Collections.Generic;
using CSharpRepl.Services.Logging;

namespace CSharpRepl.Logging;

/// <summary>
/// NullLogger is used by default. <see cref="TraceLogger"/> is used when the --trace flag is provided.
/// </summary>
internal sealed class NullLogger : ITraceLogger
{
    public void Log(
        string message
    ) { /* null logger does not log */
    }

    public void Log(
        Func<string> message
    ) { /* null logger does not log */
    }

    public void LogPaths(
        string message,
        Func<IEnumerable<string?>> paths
    ) { /* null logger does not log */
    }
}
