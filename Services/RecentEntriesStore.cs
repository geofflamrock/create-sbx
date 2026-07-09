namespace CreateSbx.Services;

/// <summary>Persists small "recently used" lists (repository URLs, workspace directory paths) to
/// <c>%AppData%/create-sbx/&lt;fileName&gt;</c>, one entry per line, most recent first.</summary>
public static class RecentEntriesStore
{
    public const string UrlsFile = "recent-urls.txt";
    public const string WorkspaceDirectoriesFile = "recent-workspace-directories.txt";

    private const int MaxEntries = 10;

    public static List<string> Load(string fileName)
    {
        var path = GetPath(fileName);
        if (!File.Exists(path))
        {
            return [];
        }

        return [.. File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l))];
    }

    public static void Add(List<string> entries, string entry, string fileName)
    {
        entries.Remove(entry);
        entries.Insert(0, entry);
        while (entries.Count > MaxEntries)
        {
            entries.RemoveAt(entries.Count - 1);
        }

        Save(fileName, entries);
    }

    private static void Save(string fileName, List<string> entries)
    {
        var path = GetPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, entries);
    }

    private static string GetPath(string fileName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "create-sbx", fileName);
}
