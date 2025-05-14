using CSharpRepl.Logging;
using CSharpRepl.Services.Roslyn.Scripting;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace Mykeels.CSharpRepl;

public static class InvokeTool
{
    public const string Name = "invoke";
    public static Type _globalsType = typeof(object);

    public static Tool Initialize(Type globalsType)
    {
        _globalsType = globalsType;
        return new Tool()
        {
            Name = Name,
            Description = "Invokes a C# command.",
            InputSchema = JsonSerializer.Deserialize<JsonElement>($@"
                {{
                    ""type"": ""object"",
                    ""properties"": {{
                        ""command"": {{
                            ""type"": ""string"",
                            ""description"": ""A C# expression to evaluate. Can be multiple statements separated by semicolons. The last statement's result is returned.""
                        }}
                    }},
                    ""required"": [""command""]
                }}
            "),
        };
    }

    public static async Task<CallToolResponse> Handle(RequestContext<CallToolRequestParams> request)
    {
        if (request.Params?.Arguments?.TryGetValue("command", out var command) is not true)
        {
            throw new McpException("Missing required argument 'command'");
        }
        string commandText = command.GetString() ?? throw new McpException("Argument 'command' must be a string");
        string expression = $"using static {_globalsType.FullName}; {commandText.TrimEnd(';')}";
        try
        {
            var result = await Repl.Evaluate(expression);
            if (result is EvaluationResult.Error error)
            {
                throw error.Exception;
            }
            else if (result is EvaluationResult.Success success)
            {
                string output = JsonLogger.LogSuccess(commandText, new { Result = success.ReturnValue.Value });
                return await Task.FromResult(new CallToolResponse()
                {
                    Content = [
                        new Content() { Text = output, Type = "text" }
                    ]
                });
            }
            else if (result is EvaluationResult.Cancelled cancelled)
            {
                throw new McpException("Evaluation cancelled");
            }
            else
            {
                throw new McpException("Invalid result type");
            }
        }
        catch (Exception exception)
        {
            string output = JsonLogger.LogError(commandText, exception, null!);
            return await Task.FromResult(new CallToolResponse()
            {
                Content = [
                    new Content() { Text = output, Type = "text" }
                ]
            });
        }
    }
}