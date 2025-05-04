namespace CSharpRepl.Services;

public static class ReferenceManager
{
    public static List<string> GetReferencePaths(string? folderPath = null)
    {
        folderPath ??= AppDomain.CurrentDomain.BaseDirectory;
        var files = Directory.GetFiles(folderPath);
        var dllFiles = files
            .Where(file => file.ToLower().EndsWith(".dll"))
            .Select(file => Path.GetRelativePath(AppDomain.CurrentDomain.BaseDirectory, file));
        return dllFiles.ToList();
    }
}

