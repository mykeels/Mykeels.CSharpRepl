using System.Collections.Generic;

namespace Mykeels.CSharpRepl;

/// <summary>
/// In-memory ring buffer of recent C# request / JSON result pairs.
/// </summary>
internal sealed class ResultHistory
{
    public const int MaxEntries = 64;

    private readonly List<ResultHistoryEntry> entries = [];

    /// <summary>
    /// Index of the entry currently being browsed, or -1 when not browsing.
    /// </summary>
    public int BrowseIndex { get; private set; } = -1;

    public int Count => entries.Count;

    public IReadOnlyList<ResultHistoryEntry> Entries => entries;

    public ResultHistoryEntry? Current =>
        BrowseIndex >= 0 && BrowseIndex < entries.Count ? entries[BrowseIndex] : null;

    public void Add(string request, string jsonResult, bool isError = false)
    {
        entries.Add(new ResultHistoryEntry(request, jsonResult, isError));
        while (entries.Count > MaxEntries)
        {
            entries.RemoveAt(0);
        }

        // Reset browse position so the next Ctrl+Up starts at the newest entry.
        BrowseIndex = -1;
    }

    /// <summary>
    /// Move to an older entry (Ctrl+Up). Returns the entry, or null if history is empty.
    /// </summary>
    public ResultHistoryEntry? MoveOlder()
    {
        if (entries.Count == 0)
            return null;

        if (BrowseIndex < 0)
            BrowseIndex = entries.Count - 1;
        else if (BrowseIndex > 0)
            BrowseIndex--;

        return Current;
    }

    /// <summary>
    /// Move to a newer entry (Ctrl+Down). Returns the entry, or null if at the newest / empty.
    /// </summary>
    public ResultHistoryEntry? MoveNewer()
    {
        if (entries.Count == 0 || BrowseIndex < 0)
            return null;

        if (BrowseIndex < entries.Count - 1)
            BrowseIndex++;

        return Current;
    }

    public void ResetBrowse() => BrowseIndex = -1;
}

internal sealed record ResultHistoryEntry(string Request, string JsonResult, bool IsError);
