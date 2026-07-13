# Mykeels.CSharpRepl

This library is a stripped-down, plug-n-play version of [CSharpRepl.Services](https://github.com/waf/CSharpRepl/tree/bd79130d49c06736a2d5f4d56ac7643889ad2328/CSharpRepl.Services). It is a powerful C# REPL (Read-Eval-Print Loop) that can be embedded into any .NET application, providing an interactive C# REPL environment with syntax highlighting, code completion, MCP server, and more.

## Installation

Copy the [nuget.config](nuget.config) file to your project directory. This is necessary to ensure that the Microsoft.SymbolStore package can be found.

```bash
dotnet add package Mykeels.CSharpRepl
```

## Quick Start

Here's a minimal example of how to use Mykeels.CSharpRepl in your application to launch a REPL:

```csharp
using Mykeels.CSharpRepl;

await Repl.Run();
```

This will start an interactive C# REPL with default settings.

## Configuration

You can customize the REPL by providing a `Configuration` object:

```csharp
using CSharpRepl.Services;
using Mykeels.CSharpRepl;
using Spectre.Console;

await Repl.Run(
    new Configuration(
        // Add references to assemblies
        references: AppDomain
            .CurrentDomain.GetAssemblies()
            .Select(a => $"{a.GetName().Name}.dll")
            .ToArray(),
        
        // Add default namespaces
        usings: [
            "System",
            "System.Collections.Generic",
            "System.IO",
            "System.Linq",
            "System.Net.Http",
            "System.Threading",
            "System.Threading.Tasks"
        ],
        
        // Set application name
        applicationName: "MyApp.CSharpRepl",
        
        // Customize success output
        logSuccess: (message, result) => {
            Console.WriteLine($"<< {message}");
            string output = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            AnsiConsole.MarkupLine($"[green]>> {output}[/]");
        },
        
        // Customize error output
        logError: (message, exception, _) => {
            Console.WriteLine($"<< {message}");
            string output = Newtonsoft.Json.JsonConvert.SerializeObject(
                new { Error = exception }
            );
            Console.WriteLine($">> {output}");
            AnsiConsole.MarkupLine($"[red]>> {output.EscapeMarkup()}[/]");
        }
    )
);
```

## Pre-execution Commands

You can specify commands to be executed before the REPL starts. This is useful for setting up the environment or importing commonly used types:

```csharp
await Repl.Run(
    commands: [
        // Import ScriptGlobals to make its methods available directly
        "using static Mykeels.CSharpRepl.Sample.ScriptGlobals;"
    ]
);
```

## ScriptGlobals

You can add your own ScriptGlobals by adding a static class with static methods and properties, and then running a pre-execution command on REPL startup.

```csharp
"using static Mykeels.CSharpRepl.Sample.ScriptGlobals;"
```

Any class named `*ScriptGlobals` that's brought into scope this way — at startup via `commands`, or by the user typing a `using static` statement mid-session — is picked up automatically by the `help` command, which lists its public static members alongside their type information (parameter types, return types, property types). No extra configuration needed; this works the same whether you're in the terminal REPL or a Slack session.

## Exploring Types with `?`

Beyond the automatic `*ScriptGlobals` listing, you can introspect anything on demand by typing `<expr> ?` — the member list (with type information) prints the same way `help` prints one for `*ScriptGlobals`:

```csharp
DateTime ?      // an unqualified type name, resolved via an active `using`
System.DateTime ?   // a fully-qualified type name
Http ?          // an in-scope expression — evaluated, then its runtime type is introspected
```

- If `<expr>` resolves to a type name (fully-qualified, or unqualified via a namespace already brought into scope with `using`), that type's members are listed directly.
- Otherwise, `<expr>` is evaluated as C# and the *runtime type of the result* is introspected instead — this is what makes `Http ?` useful: `Http` is a property (e.g. from `ScriptGlobals`), so it's evaluated and whatever it returns (an `HttpClient`, say) gets introspected.
- If neither works, you'll get a "Couldn't resolve" message instead of a compile error.

This works the same in the terminal REPL and in a Slack session, and is the quickest way to explore what's available without leaving the REPL — e.g. `Http ?` to see what methods an injected client offers, or `SomeResult ?` to check what a previous expression's return value looks like.

## MCP Server

You can also launch a MCP server that can be used to:

- list members of the ScriptGlobals class
- invoke arbitrary C# code, written with the ScriptGlobals class as the globals context

```csharp
await McpServer.Run(typeof(ScriptGlobals));
```

Such an MCP server can be used by a tool like [Cursor](https://www.cursor.com/) to give Cursor Chat the ability to execute C# code.

## Slack Integration

You can host the REPL over Slack instead of (or as well as) a terminal: a user runs a slash command in Slack, the bot starts a thread, and messages sent in that thread are evaluated as REPL input, with results/errors posted back as replies. Multiple threads can each run their own independent session.

### 1. Create the Slack app

The easiest way is from a manifest — go to [api.slack.com/apps](https://api.slack.com/apps) → **Create New App → From an app manifest**, and paste in:

```json
{
  "display_information": {
    "name": "Mykeels C# REPL",
    "description": "Run a C# REPL session in a Slack thread",
    "background_color": "#2c2d30"
  },
  "features": {
    "bot_user": {
      "display_name": "mykeels-csharp-repl",
      "always_online": true
    },
    "slash_commands": [
      {
        "command": "/mykeels-csharp-repl",
        "description": "Start a new C# REPL session in a thread",
        "usage_hint": "new",
        "should_escape": false
      }
    ]
  },
  "oauth_config": {
    "scopes": {
      "bot": ["chat:write", "commands", "channels:history", "groups:history", "im:history", "files:write"]
    }
  },
  "settings": {
    "event_subscriptions": {
      "bot_events": ["message.channels", "message.groups", "message.im"]
    },
    "interactivity": {
      "is_enabled": true
    },
    "org_deploy_enabled": false,
    "socket_mode_enabled": true,
    "token_rotation_enabled": false
  }
}
```

Notes on the manifest:

- `interactivity.is_enabled: true` is required even though this app has no buttons/modals — Slack routes slash-command payloads through the interactivity pipeline when Socket Mode is on, so it won't deliver them without this.
- Each `message.*` event needs its matching `*:history` scope to read message content in that conversation type: `message.channels` → `channels:history`, `message.groups` → `groups:history`, `message.im` → `im:history`. If you only need sessions in public channels, drop `message.groups`/`message.im` and their scopes.

A manifest can't do everything, though — after creating the app:

1. **Generate an app-level token.** Under **Basic Information → App-Level Tokens**, create one with the `connections:write` scope (starts with `xapp-`). This is what lets `SlackReplHost` use Socket Mode's WebSocket connection — the bot token alone isn't enough, and there's no way to bake this into the manifest since it's generated per-install.
2. **Install the app to your workspace.** Under **OAuth & Permissions**, install it to get a bot token (starts with `xoxb-`).
3. **Invite the bot** to whichever channels it should work in.

(If you'd rather configure it by hand instead of from a manifest: enable Socket Mode; add the bot token scopes above under OAuth & Permissions; create the slash command under Slash Commands — no Request URL needed with Socket Mode; enable Event Subscriptions and subscribe to the `message.*` events you need. Then do the three steps above.)

### 2. Run the host

```csharp
using Mykeels.CSharpRepl;

await SlackReplHost.Run(
    new SlackReplOptions
    {
        BotToken = Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN")!,
        AppToken = Environment.GetEnvironmentVariable("SLACK_APP_TOKEN")!,
        SlashCommand = "/mykeels-csharp-repl", // must match what you registered above
        AllowedChannelIds = ["C0123456789"], // or AllowedUserIds — at least one is required, see below
    }
);
```

`SlackReplHost.Run` connects over Socket Mode and runs until cancelled (pass a `CancellationToken` to stop it), starting a new `Repl.Run` session — with its own `RoslynServices`, independent of any other open session — for every slash command invocation.

### 3. Authorization

At least one of `AllowedUserIds` or `AllowedChannelIds` must be set — `SlackReplHost` refuses to start otherwise, since anyone able to run the slash command would otherwise get a REPL with the same code-execution privileges as the host process:

- `AllowedUserIds` / `AllowedChannelIds`: `HashSet<string>?` of Slack user/channel IDs allowed to start and use sessions. Leaving one unset doesn't restrict by it — e.g. setting only `AllowedChannelIds` allows any user in those channels.
- `RestrictRepliesToSessionOwner` (default `true`): only the user who ran the slash command can send messages into their own session's thread.
- `IsAuthorized`: optional `Func<SlackAuthorizationContext, bool>` for additional checks (e.g. against an external ACL), evaluated in addition to the allowlists above, not instead of them.
- `IdleTimeout`: optional `TimeSpan` — a session thread with no activity for this long is closed automatically, so an abandoned thread doesn't hold a `RoslynServices` alive forever.

Run `dotnet run -- slack` in `Mykeels.CSharpRepl.Sample` (with `SLACK_BOT_TOKEN`, `SLACK_APP_TOKEN`, and optionally `SLACK_ALLOWED_CHANNEL_IDS` set as a comma-separated list) to try it.

## Features

- **Syntax Highlighting**: Code is colorized for better readability
- **Code Completion**: Intelligent code completion with IntelliSense
- **Error Handling**: Detailed error messages with stack traces
- **JSON Output**: Results are automatically serialized to JSON
- **Customizable**: Configure references, namespaces, and output formatting
- **Interactive**: Full C# interactive environment with REPL capabilities

## Configuration Options

The `Configuration` class supports the following options:

- `references`: Array of assembly references to load
- `usings`: Array of namespaces to import by default
- `applicationName`: Name of your application
- `logSuccess`: Callback for handling successful evaluations
- `logError`: Callback for handling evaluation errors
- `commands`: Array of commands to execute before starting the REPL

## Examples

### Basic Usage

```csharp
await Repl.Run();
```

### With Custom References

```csharp
await Repl.Run(
    new Configuration(
        references: ["MyApp.dll", "MyApp.Models.dll"]
    )
);
```

### With Custom Output Formatting

```csharp
await Repl.Run(
    new Configuration(
        logSuccess: (message, result) => {
            Console.WriteLine($"Input: {message}");
            Console.WriteLine($"Result: {result}");
        }
    )
);
```

### With Pre-execution Commands

```csharp
await Repl.Run(
    commands: [
        "using static Mykeels.CSharpRepl.Sample.ScriptGlobals;",
        "var greeting = \"Hello, World!\";"
    ]
);
```

## Best Practices

1. **Assembly References**: Include all necessary assemblies in the `references` array
2. **Namespaces**: Add commonly used namespaces to the `usings` array
3. **Error Handling**: Implement custom error handling in `logError` for better debugging
4. **Output Formatting**: Use `AnsiConsole` for colored output and better readability
5. **Pre-execution Commands**: Use `commands` to set up your environment and import commonly used types

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
