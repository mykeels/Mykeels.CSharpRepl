using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Spectre.Console;
using System.ComponentModel;

namespace Mykeels.CSharpRepl.Sample;

/// <summary>
/// Provides access to common services and utilities for the REPL.
/// All static properties and methods in this class are automatically available in the REPL.
/// </summary>
public static class ScriptGlobals
{
    /// <summary>
    /// Gets a new instance of HttpClient for making HTTP requests.
    /// </summary>
    public static HttpClient Http => new();

    public static string GetCurrentDirectory() => CurrentDirectory;

    /// <summary>
    /// Gets the current working directory.
    /// </summary>
    public static string CurrentDirectory => Directory.GetCurrentDirectory();

    /// <summary>
    /// Gets a list of files in the current directory.
    /// </summary>
    public static IEnumerable<string> Files => Directory.GetFiles(CurrentDirectory);

    /// <summary>
    /// Gets a list of directories in the current directory.
    /// </summary>
    public static IEnumerable<string> Directories => Directory.GetDirectories(CurrentDirectory);

    /// <summary>
    /// Pretty prints an object to the console using JSON formatting.
    /// </summary>
    /// <param name="obj">The object to print.</param>
    public static void Print(object? obj)
    {
        if (obj is null)
        {
            AnsiConsole.MarkupLine("[red]null[/]");
            return;
        }

        var json = JsonConvert.SerializeObject(obj, Formatting.Indented);
        AnsiConsole.MarkupLine($"[green]{json}[/]");
    }

    /// <summary>
    /// Creates a table from a collection of objects.
    /// </summary>
    /// <typeparam name="T">The type of objects in the collection.</typeparam>
    /// <param name="items">The collection of items to display.</param>
    /// <param name="title">Optional title for the table.</param>
    public static void Table<T>(IEnumerable<T> items, string? title = null)
    {
        var table = new Table();
        
        if (!string.IsNullOrEmpty(title))
        {
            table.Title = new TableTitle(title);
        }

        // Get properties of T
        var properties = typeof(T).GetProperties();
        
        // Add columns
        foreach (var prop in properties)
        {
            table.AddColumn(prop.Name);
        }

        // Add rows
        foreach (var item in items)
        {
            var values = properties.Select(p => p.GetValue(item)?.ToString() ?? "null");
            table.AddRow(values.ToArray());
        }

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Clears the console.
    /// </summary>
    public static void Clear() => Console.Clear();

    /// <summary>
    /// Gets the current date and time.
    /// </summary>
    public static DateTime Now => DateTime.Now;

    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    public static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>
    /// Creates a new directory.
    /// </summary>
    /// <param name="path">The path of the directory to create.</param>
    public static void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="path">The path of the file to delete.</param>
    public static void DeleteFile(string path) => File.Delete(path);

    /// <summary>
    /// Deletes a directory.
    /// </summary>
    /// <param name="path">The path of the directory to delete.</param>
    /// <param name="recursive">Whether to delete subdirectories and files.</param>
    public static void DeleteDirectory(string path, bool recursive = false) => Directory.Delete(path, recursive);

    /// <summary>
    /// Reads all text from a file.
    /// </summary>
    /// <param name="path">The path of the file to read.</param>
    /// <returns>The contents of the file.</returns>
    public static string ReadFile(string path) => File.ReadAllText(path);

    /// <summary>
    /// Writes text to a file.
    /// </summary>
    /// <param name="path">The path of the file to write.</param>
    /// <param name="contents">The contents to write.</param>
    public static void WriteFile(string path, string contents) => File.WriteAllText(path, contents);

    /// <summary>
    /// Downloads a file from a URL.
    /// </summary>
    /// <param name="url">The URL to download from.</param>
    /// <param name="path">The path to save the file to.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task DownloadFile(string url, string path)
    {
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(path);
        await stream.CopyToAsync(fileStream);
    }
} 