using CSharpRepl.Services;
using Mykeels.CSharpRepl;
using Mykeels.CSharpRepl.Sample;

if (args.Contains("repl"))
{
    await Repl.Run(
        commands: [
            "using static Mykeels.CSharpRepl.Sample.ScriptGlobals;",
            "using Mykeels.CSharpRepl.Sample;"
        ]
    );
}
else if (args.Contains("slack"))
{
    await SlackReplHost.Run(
        new SlackReplOptions
        {
            BotToken = RequireEnvironmentVariable("SLACK_BOT_TOKEN"),
            AppToken = RequireEnvironmentVariable("SLACK_APP_TOKEN"),
            // Fails closed if left unset — see SlackReplOptions.AllowedUserIds.
            AllowedChannelIds = Environment
                .GetEnvironmentVariable("SLACK_ALLOWED_CHANNEL_IDS")
                ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(),
        },
        commands: [
            "using static Mykeels.CSharpRepl.Sample.ScriptGlobals;",
            "using Mykeels.CSharpRepl.Sample;"
        ]
    );
}
else
{
    await McpServer.Run(typeof(ScriptGlobals));
}

static string RequireEnvironmentVariable(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Set the {name} environment variable to run the Slack sample.");
