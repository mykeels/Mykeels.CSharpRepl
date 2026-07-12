# Slack integration for Mykeels.CSharpRepl

## Goal

Let a consumer opt into a Slack-hosted REPL with one call:

```csharp
await Repl.Run(config, commands, onLoad, console: new SlackConsoleEx(slackOptions));
```

UX in Slack:

1. A user runs `/new-csharp-repl` in a channel.
2. The bot starts a thread ("🖥️ New C# REPL session started...") and a fresh `RoslynServices` instance is created for it.
3. The user sends messages in that thread; each message is evaluated as a REPL entry, and results/errors are posted back as replies in the same thread.
4. Sending `exit` (matching today's built-in command) ends the session; the bot posts a closing message and the thread stops being tracked.

Multiple threads can be open concurrently (multiple users, multiple sessions), each with its own `RoslynServices`/state, isolated from the others.

## Key architectural constraint

`Repl.Run` today always drives the loop through `PrettyPrompt.Prompt`, which is fundamentally a **terminal** abstraction: it reads raw key presses one at a time via `PrettyPrompt.Consoles.IConsole` and does its own cursor/ANSI rendering for syntax highlighting, multi-line editing, tab completion, etc. There is no Slack analogue for "key press" — Slack only gives us whole messages. Trying to adapt `IConsole` to synthesize key presses from a posted message would be fragile and pointless, since none of PrettyPrompt's live-editing features (completion popups, F1/F9/F12, in-place syntax highlighting) make sense in a chat thread anyway.

This isn't a novel problem for this codebase — `Mykeels.CSharpRepl/MCP/McpServer.cs` already sidesteps `PrettyPrompt` entirely for its non-terminal use case, driving `RoslynServices` directly instead of going through `ReadEvalPrintLoop`/`Prompt`. The Slack integration follows the same precedent: **for message-based consoles, skip `PrettyPrompt.Prompt` and drive `RoslynServices.EvaluateAsync` from a much simpler message loop.**

## Interface changes

### `IConsoleEx` (minimal, backward-compatible addition)

Add one default-interface member so `Repl.Run` can decide which loop to use, without breaking `SystemConsoleEx` or any other existing implementer:

```csharp
bool IsInteractive => true; // default keeps existing terminal-based behavior
```

`SlackConsoleEx` overrides this to `false`.

`SlackConsoleEx.PrettyPromptConsole` still needs to return *something* (the interface requires it), since a couple of default methods (`WriteError`, `WriteErrorLine`) read `PrettyPromptConsole.IsErrorRedirected`. A small internal stub (`IsErrorRedirected => false`, everything else no-op/throw `NotSupportedException`) is enough — it's never handed to a real `Prompt`.

### New small optional interface for async, queued reads

`IConsoleEx.ReadLine()` is synchronous — fine for `Console.ReadLine()`, but blocking a thread indefinitely per open Slack thread doesn't scale (many concurrent sessions = many parked threads, and this is also the wrong tool since Repl.Run is `async`). Add a narrow interface that `SlackConsoleEx` implements in addition to `IConsoleEx`:

```csharp
internal interface IAsyncLineConsole
{
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken); // see "Output buffering" below
}
```

The new message loop (below) pattern-matches: `if (console is IAsyncLineConsole asyncConsole)` and awaits properly; otherwise it falls back to `console.ReadLine()` on a dedicated long-running task, so the abstraction still works for any other future non-interactive `IConsoleEx`.

### `Repl.Run` branch

```csharp
IConsoleEx console = console ?? new SystemConsoleEx();
...
if (console.IsInteractive)
{
    // existing InitializePrompt(...) + ReadEvalPrintLoop(...).RunAsync(...) path, unchanged
}
else
{
    return await new MessageReadEvalPrintLoop(console, roslyn)
        .RunAsync(config, commands, onLoad)
        .ConfigureAwait(false);
}
```

## New components

```text
Mykeels.CSharpRepl/
  Slack/
    SlackConsoleEx.cs        # IConsoleEx + IAsyncLineConsole adapter bound to one (channel, thread_ts)
    SlackConsole.cs          # minimal PrettyPrompt.Consoles.IConsole stub (IsErrorRedirected=false, rest NotSupportedException)
    SlackReplHost.cs         # owns the SlackNet Socket Mode client; slash command + thread routing; session table
    SlackReplOptions.cs      # bot token, app-level token, allowed channels/users, session idle timeout, etc.
  Repl/
    MessageReadEvalPrintLoop.cs   # ReadLine -> EvaluateAsync -> Print loop, no PrettyPrompt.Prompt involved
```

### `MessageReadEvalPrintLoop`

Mirrors `ReadEvalPrintLoop.RunAsync` but reads whole messages instead of `prompt.ReadLineAsync()`:

- Reuse the existing `PrintAsync`/help text formatting logic — refactor the `private static PrintAsync` in `ReadEvalPrintLoop` to `internal static` (same assembly, `Mykeels.CSharpRepl` namespace) so both loops share it instead of duplicating.
- Same built-in commands: `exit` ends the loop; `help`/`?` posts the help text. `clear` is a no-op for a chat thread (there's nothing to clear) — reply with a short "not applicable in Slack" note instead of silently ignoring it, so it's not confusing.
- After each turn (built-in command, prompt callback, or evaluation result), call `console.FlushAsync()` to flush buffered output as a single Slack reply (see below), then block on the next `ReadLineAsync()`.

### `SlackConsoleEx`

- Constructed per-session by `SlackReplHost` with the `SlackNet` client, `channel`, `thread_ts`, and an inbound `System.Threading.Channels.Channel<string>`.
- `ReadLineAsync`: `await inbound.Reader.ReadAsync(cancellationToken)` — `SlackReplHost`'s message-event handler writes incoming thread replies into this channel.
- Output (`Write(IRenderable)`, `Write(FormattedString)`, `WriteLine(...)`, `WriteError*`): all funnel into a per-turn `StringBuilder`. `IRenderable` is rendered through a private `Spectre.Console.IAnsiConsole` created via `AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(stringWriter), ColorSystem = ColorSystemSupport.NoColors })` — this keeps table/panel layout (box-drawing characters, alignment) but strips ANSI color codes, since Slack code blocks don't render ANSI. `FormattedString` writes go through the same route (its plain-text projection).
- `FlushAsync`: if the buffer is non-empty, post it to Slack via `chat.postMessage` with `thread_ts` set, wrapped in a triple-backtick code block for monospacing, then clear the buffer.
  - Slack messages are capped at 40,000 characters; truncate (with a `"... (truncated, N more characters)"` marker) rather than trying to paginate across multiple messages for v1.
- `Clear(bool home)`: no-op (nothing to clear in a thread).
- `ReadLine()` (sync fallback, required by `IConsoleEx`): `ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult()` — present for interface completeness, not expected to be called by `MessageReadEvalPrintLoop` since it prefers `IAsyncLineConsole`.

### `SlackReplHost`

- Wraps a `SlackNet` `ISlackSocketModeClient` (Socket Mode, per your choice — no public endpoint needed, fits this being a library/CLI feature rather than requiring the consumer to also stand up a webhook receiver).
- Registers a slash-command handler for `/new-csharp-repl` (name configurable via `SlackReplOptions`):
  1. Posts the "session started" message to the invoking channel; captures its `ts` as `thread_ts`.
  2. Creates the inbound `Channel<string>` + `SlackConsoleEx`.
  3. Starts `Repl.Run(config, commands, onLoad, console: consoleEx)` on a **dedicated long-running task** (`Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)`), not a plain `Task.Run`, since each session lives for the lifetime of the thread and otherwise could pin thread-pool threads.
  4. Tracks the session in `ConcurrentDictionary<(string channel, string threadTs), SlackReplSession>`.
- Registers a message-event handler: for every message, if `(channel, thread_ts)` matches a tracked session and the sender isn't the bot itself (avoid echoing its own replies back into the REPL), write the message text into that session's inbound channel.
- On the session's `Repl.Run` task completing (whether via `exit` or an unhandled fault), removes it from the dictionary and posts a closing message ("✅ Session ended" / "⚠️ Session crashed: ...").
- Optional (recommended, flag via `SlackReplOptions`): idle timeout per session (no message for N minutes -> auto-close) so abandoned threads don't hold a `RoslynServices` (and its loaded assemblies) alive forever.

## Auth / allowlisting

Anyone who can run a slash command in a shared workspace would otherwise get a REPL with the same code-execution privileges as the host process — this is arbitrary code execution surfaced to any Slack user, so it needs to be a first-class part of `SlackReplOptions`, not bolted on later.

### `SlackReplOptions` additions

```csharp
public sealed class SlackReplOptions
{
    ...
    public HashSet<string>? AllowedUserIds { get; init; }      // Slack user IDs allowed to start/drive sessions
    public HashSet<string>? AllowedChannelIds { get; init; }   // channels where /new-csharp-repl may be invoked
    public bool RestrictRepliesToSessionOwner { get; init; } = true;
    public Func<SlackAuthorizationContext, bool>? IsAuthorized { get; init; } // optional escape hatch, see below
}

public readonly record struct SlackAuthorizationContext(string UserId, string ChannelId);
```

- **`AllowedUserIds` / `AllowedChannelIds`**: both nullable, both default to `null`. Unlike most optional config, **`null`/empty means fail closed, not allow-all** — if neither is configured, `SlackReplHost.Run` throws at startup (or logs a loud warning and refuses to register the slash command handler) rather than silently exposing code execution to the whole workspace. A consumer who genuinely wants "anyone in the workspace" has to opt in explicitly, e.g. `AllowedChannelIds = ["*"]` or a dedicated `AllowAnyUserInAllowedChannels = true` flag — the point is the insecure default has to be typed out, not fallen into.
- **`IsAuthorized`**: an optional additional predicate (e.g. to call out to an external ACL service or a dynamically-reloaded list). When set, it's evaluated in addition to (AND'd with) the allowlists, not instead of them — the allowlists remain the baseline gate.

### Enforcement points

1. **Slash command handler** (`/new-csharp-repl`): before creating a session, check the invoking `user_id` against `AllowedUserIds` and the `channel_id` against `AllowedChannelIds` (plus `IsAuthorized` if configured). If denied, respond with an ephemeral message (`chat.postEphemeral`, visible only to the invoker — doesn't leak the attempt to the whole channel) explaining they're not authorized, and do not start a `RoslynServices`/`Repl.Run` session at all.
2. **Message-event handler, on every incoming thread reply, not just at session creation**: re-check the sender against `AllowedUserIds`/`IsAuthorized` (and, if `RestrictRepliesToSessionOwner` is true, against the specific session's `OwnerUserId`) before writing the message into the session's inbound channel. This is deliberately re-checked per message rather than only once at session start, so that:
   - revoking a user's access (updating the allowlist / backing ACL service) takes effect immediately, without waiting for open sessions to end;
   - by default, other users in the same channel can see the thread but can't drive someone else's REPL session just by replying in it — only the user who ran `/new-csharp-repl` can submit code, unless `RestrictRepliesToSessionOwner` is explicitly turned off.
   - a denied reply gets a short ephemeral note ("only the user who started this session can send commands here") rather than being silently dropped, so it's not confusing.
3. **Audit logging**: every evaluated message should be traceable to `userId`/`channelId`/`threadTs`, given the blast radius of arbitrary code execution. This can't simply reuse `Configuration.LogSuccess`/`LogError` as-is, though — implemented (see `Repl/ReadEvalPrintLoop.cs::PrintAsync`), a non-null `LogSuccess` *replaces* writing the result to `console` rather than supplementing it, and `JsonLogger` (the default `LogSuccess`/`LogError`) writes straight to the real process `Console`/`AnsiConsole.Console`, not to whichever `IConsoleEx` is active — harmless for a real terminal (same console either way) but silently drops output for a message-based console. `MessageReadEvalPrintLoop` (phase 1) sidesteps this by not passing `commandText` into `PrintAsync`, which keeps it on the `console.Write(formatted)` path unconditionally. For Slack audit logging specifically, don't route through `Configuration.LogSuccess`/`LogError` — instead have `SlackReplHost`/`SlackConsoleEx` log directly (e.g. wrap `RoslynServices.EvaluateAsync` or log in the message-event handler) so it doesn't interfere with output delivery.

## Dependencies

- Add `SlackNet` NuGet package to `Mykeels.CSharpRepl.csproj`.
- Requires a Slack app configured with: Socket Mode enabled, an app-level token (`connections:write` scope) for the socket, a bot token with `chat:write` and `commands` scopes, and the `/new-csharp-repl` slash command registered pointing at Socket Mode (no Request URL needed).

## Sample usage (mirrors `Mykeels.CSharpRepl.Sample/Program.cs`)

```csharp
if (args.Contains("slack"))
{
    await SlackReplHost.Run(new SlackReplOptions
    {
        BotToken = Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN")!,
        AppToken = Environment.GetEnvironmentVariable("SLACK_APP_TOKEN")!,
    }, config: new Configuration(...), commands: [...]);
}
```

(`SlackReplHost.Run` starts the socket connection and blocks/awaits until cancelled — analogous to `McpServer.Run`.)

## Phased implementation

1. **Non-Slack plumbing**: add `IConsoleEx.IsInteractive`, extract `MessageReadEvalPrintLoop` from `ReadEvalPrintLoop` (share `PrintAsync`), branch in `Repl.Run`. Verify with a throwaway in-memory `IConsoleEx` test double (e.g. a fake fed from a `List<string>` of "messages") before touching Slack at all — this isolates the "no PrettyPrompt" loop from the Slack transport.
2. **`SlackConsoleEx` + `SlackConsole` stub**: implement output buffering/formatting and the async read/flush contract, unit-testable against a fake `SlackNet` client (no real Slack calls).
3. **`SlackReplHost`**: Socket Mode wiring, slash command handler, message routing, session table, idle timeout, and the auth/allowlist enforcement described above (fail-closed defaults, per-message re-check, ephemeral denial messages).
4. **Docs + sample**: add `Mykeels.CSharpRepl.Sample` opt-in path and a README section describing Slack app setup (scopes, tokens, slash command registration).

## Open questions / risks (flag before/at build time, not blocking this plan doc)

- **Output streaming vs batching**: flushing only between turns means long-running user code (e.g. a loop with many `Console.WriteLine` calls) won't show partial output until it finishes. Acceptable for v1; a periodic flush (e.g. every 2s or every N buffered chars) is a natural follow-up if it proves annoying.
- **Concurrent messages in one thread**: if a user sends a second message in the thread before the first evaluation finishes, it just queues in the inbound channel and gets picked up on the next `ReadLineAsync` — no cancellation of in-flight evaluation. Fine for v1; "send a message to cancel the current one" would need extra plumbing (compare to `ExitApplicationKeyPress`/cancellation token handling in the terminal loop).
- **Auth/allowlisting**: see the dedicated "Auth / allowlisting" section above — the fail-closed default and per-message re-check need to land in phase 3 alongside `SlackReplHost`, not be deferred to a v2.
- **Multiple bot instances**: if `SlackReplHost.Run` is started more than once against the same Slack app/socket, slash commands could be double-handled. Out of scope for this plan; assume single-instance hosting for now.
