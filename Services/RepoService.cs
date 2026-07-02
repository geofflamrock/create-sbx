using System.Text.RegularExpressions;
using CreateSbx.Models;

namespace CreateSbx.Services;

public static partial class RepoService
{
    public static async Task<string> EnsureRepoAsync(
        string owner,
        string repo,
        string branch,
        HashSet<string> fetchedRepos,
        Action<string> onStatus)
    {
        var cloneDir = Path.Combine(Path.GetTempPath(), "create-sbx", owner, repo);
        var repoKey = $"{owner}/{repo}#{branch}";

        if (fetchedRepos.Contains(repoKey))
        {
            return cloneDir;
        }

        if (Directory.Exists(Path.Combine(cloneDir, ".git")))
        {
            onStatus("Fetching latest changes...");
            if (string.IsNullOrEmpty(branch))
            {
                await ProcessRunner.RunAsync("git", ["fetch", "origin"], cloneDir);
            }
            else
            {
                await ProcessRunner.RunAsync("git", ["fetch", "origin", $"+refs/heads/{branch}:refs/remotes/origin/{branch}"], cloneDir);
            }
        }
        else
        {
            onStatus("Cloning repository...");
            Directory.CreateDirectory(Path.GetDirectoryName(cloneDir)!);
            await ProcessRunner.RunAsync("git", ["clone", $"https://github.com/{owner}/{repo}.git", cloneDir]);
        }

        if (string.IsNullOrEmpty(branch))
        {
            await ProcessRunner.RunAsync("git", ["checkout", "--detach", "origin/HEAD"], cloneDir);
        }
        else
        {
            await ProcessRunner.RunAsync("git", ["checkout", "--detach", $"origin/{branch}"], cloneDir);
        }

        fetchedRepos.Add(repoKey);
        return cloneDir;
    }

    public static List<Kit> FindKits(string cloneDir)
    {
        var kits = new List<Kit>();

        var rootSpec = Path.Combine(cloneDir, "spec.yaml");
        if (File.Exists(rootSpec))
        {
            var specYaml = File.ReadAllText(rootSpec);
            var displayName = ParseDisplayName(specYaml) ?? Path.GetFileName(cloneDir);
            var description = ParseDescription(specYaml);
            kits.Add(new Kit(null, displayName!, description));
        }

        foreach (var dir in Directory.GetDirectories(cloneDir).Order())
        {
            var dirName = Path.GetFileName(dir)!;
            if (dirName.StartsWith('.'))
            {
                continue;
            }

            var specFile = Path.Combine(dir, "spec.yaml");
            if (!File.Exists(specFile))
            {
                continue;
            }

            var specYaml = File.ReadAllText(specFile);
            var displayName = ParseDisplayName(specYaml) ?? dirName;
            var description = ParseDescription(specYaml);
            kits.Add(new Kit(dirName, displayName, description));
        }

        return kits;
    }

    public static List<string> FindDockerfiles(string repoDir)
    {
        var results = new List<string>();
        FindDockerfilesRecursive(repoDir, repoDir, results);
        results.Sort();
        return results;
    }

    private static void FindDockerfilesRecursive(string baseDir, string currentDir, List<string> results)
    {
        foreach (var file in Directory.GetFiles(currentDir))
        {
            var fileName = Path.GetFileName(file);
            if (IsDockerfileName(fileName))
            {
                results.Add(Path.GetRelativePath(baseDir, file));
            }
        }

        foreach (var dir in Directory.GetDirectories(currentDir))
        {
            if (Path.GetFileName(dir)!.StartsWith('.'))
            {
                continue;
            }

            FindDockerfilesRecursive(baseDir, dir, results);
        }
    }

    private static bool IsDockerfileName(string fileName) =>
        fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
        fileName.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".dockerfile", StringComparison.OrdinalIgnoreCase);

    private static string? ParseDisplayName(string yaml)
    {
        var displayMatch = DisplayNameRegex().Match(yaml);
        if (displayMatch.Success)
        {
            return displayMatch.Groups[1].Value.Trim().Trim('"');
        }

        var nameMatch = NameRegex().Match(yaml);
        if (nameMatch.Success)
        {
            return nameMatch.Groups[1].Value.Trim().Trim('"');
        }

        return null;
    }

    private static string? ParseDescription(string yaml)
    {
        var match = DescriptionRegex().Match(yaml);
        return match.Success ? match.Groups[1].Value.Trim().Trim('"') : null;
    }

    [GeneratedRegex(@"^displayName:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex DisplayNameRegex();

    [GeneratedRegex(@"^name:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^description:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex DescriptionRegex();
}
