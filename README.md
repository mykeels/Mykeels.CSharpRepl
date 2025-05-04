# Mykeels.CSharpRepl

This library is a stripped-down, plug-n-play version of [CSharpRepl.Services](https://github.com/
waf/CSharpRepl/tree/bd79130d49c06736a2d5f4d56ac7643889ad2328/CSharpRepl.Services). It is a powerful C# REPL (Read-Eval-Print Loop) that can be embedded into any .NET application, providing an interactive C# environment with syntax highlighting, code completion, and more.

## Installation

```bash
dotnet add package Mykeels.CSharpRepl
```

## Quick Start

Here's a minimal example of how to use Mykeels.CSharpRepl in your application:

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

## Best Practices

1. **Assembly References**: Include all necessary assemblies in the `references` array
2. **Namespaces**: Add commonly used namespaces to the `usings` array
3. **Error Handling**: Implement custom error handling in `logError` for better debugging
4. **Output Formatting**: Use `AnsiConsole` for colored output and better readability

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
