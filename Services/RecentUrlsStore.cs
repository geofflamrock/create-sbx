namespace CreateSbx.Services;

public static class RecentUrlsStore
{
    private const int MaxEntries = 10;

    public static string GetPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "create-sbx", "recent-urls.txt");

    public static List<string> Load()
    {
        var path = GetPath();
        if (!File.Exists(path))
        {
            return [];
        }

        return [.. File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l))];
    }

    public static void Add(List<string> urls, string url)
    {
        urls.Remove(url);
        urls.Insert(0, url);
        while (urls.Count > MaxEntries)
        {
            urls.RemoveAt(urls.Count - 1);
        }

        Save(urls);
    }

    private static void Save(List<string> urls)
    {
        var path = GetPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, urls);
    }
}
