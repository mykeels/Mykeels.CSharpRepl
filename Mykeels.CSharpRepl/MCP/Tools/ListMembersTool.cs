using ModelContextProtocol;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace Mykeels.CSharpRepl;

public static class ListMembersTool
{
    public static string Name = "list";

    public static Tool Initialize(Type globalsType)
    {
        return new Tool()
        {
            Name = Name,
            Description = "Lists all public C# members of a type i.e. properties and methods that can be invoked.",
            InputSchema = JsonSerializer.Deserialize<JsonElement>($@"
                {{
                    ""type"": ""object"",
                    ""properties"": {{
                        ""type"": {{
                            ""type"": ""string"",
                            ""description"": ""The type to list components for. Defaults to the type of the globals object."",
                            ""default"": ""{globalsType.FullName}"",
                            ""nullable"": true
                        }}
                    }},
                    ""required"": []
                }}
            "),
        };
    }

    public static async Task<CallToolResponse> Handle(RequestContext<CallToolRequestParams> request)
    {
        if (request.Params?.Arguments?.TryGetValue("type", out var type) is not true)
        {
            throw new McpException("Missing required argument 'type'");
        }
        string typeName = type.GetString() ?? throw new McpException("Argument 'type' must be a string");
        var classType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(a => a.GetType(typeName))
            .FirstOrDefault(t => t != null);
        if (classType is null)
        {
            throw new McpException($"Type not found: '{typeName}'");
        }

        var components = Introspector.ListComponents(classType);

        return await Task.FromResult(new CallToolResponse()
        {
            Content = components
                .Select(component => new Content() { Text = component, Type = "text" })
                .ToList()
        });
    }
}