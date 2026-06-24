using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

var repoUrl = AnsiConsole.Ask<string>("Enter the [green]GitHub repository URL[/] containing sbx kits:");
repoUrl = repoUrl.Trim().TrimEnd('/');

var (owner, repo) = ParseGitHubUrl(repoUrl);
if (owner is null || repo is null)
{
    AnsiConsole.MarkupLine("[red]Invalid GitHub repository URL. Expected format: https://github.com/owner/repo[/]");
    return 1;
}

List<Kit> kits = [];

await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .StartAsync("Searching for kits...", async ctx =>
    {
        kits = await FindKits(owner, repo);
    });

if (kits.Count == 0)
{
    AnsiConsole.MarkupLine("[yellow]No kits found in the repository.[/]");
    return 0;
}

AnsiConsole.MarkupLine($"[green]Found {kits.Count} kit(s).[/]");

var selected = AnsiConsole.Prompt(
    new MultiSelectionPrompt<Kit>()
        .Title("Select the [green]kits[/] to include:")
        .NotRequired()
        .PageSize(20)
        .MoreChoicesText("[grey](Move up and down to reveal more kits)[/]")
        .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
        .AddChoices(kits)
        .UseConverter(k => k.DisplayName));

if (selected.Count == 0)
{
    AnsiConsole.MarkupLine("[yellow]No kits selected.[/]");
    return 0;
}

var gitUrl = $"git+https://github.com/{owner}/{repo}.git";
var kitFlags = string.Join(" ", selected.Select(k => $"--kit \"{gitUrl}#dir={k.Directory}\""));
var command = $"sbx run {kitFlags}";

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[bold]Run this command to create your sandbox:[/]");
AnsiConsole.MarkupLine($"[blue]{Markup.Escape(command)}[/]");

return 0;

static (string? owner, string? repo) ParseGitHubUrl(string url)
{
    var match = Regex.Match(url, @"github\.com[/:](?<owner>[^/]+)/(?<repo>[^/.]+)");
    if (!match.Success) return (null, null);
    return (match.Groups["owner"].Value, match.Groups["repo"].Value);
}

static async Task<List<Kit>> FindKits(string owner, string repo)
{
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("create-sbx/1.0");

    var treeUrl = $"https://api.github.com/repos/{owner}/{repo}/git/trees/main?recursive=1";
    var treeJson = await http.GetStringAsync(treeUrl);

    using var doc = JsonDocument.Parse(treeJson);
    var tree = doc.RootElement.GetProperty("tree");

    // Find all spec.yaml files that are directly inside a top-level directory
    // (i.e. path is "<dir>/spec.yaml", not nested deeper)
    var specPaths = new List<string>();
    foreach (var item in tree.EnumerateArray())
    {
        if (item.GetProperty("type").GetString() != "blob") continue;
        var path = item.GetProperty("path").GetString()!;

        var parts = path.Split('/');
        if (parts.Length == 2 && parts[1].Equals("spec.yaml", StringComparison.OrdinalIgnoreCase))
            specPaths.Add(path);
    }

    var kits = new List<Kit>();
    foreach (var specPath in specPaths)
    {
        var dir = specPath.Split('/')[0];
        var rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/main/{specPath}";

        try
        {
            var specYaml = await http.GetStringAsync(rawUrl);
            var displayName = ParseDisplayName(specYaml) ?? dir;
            kits.Add(new Kit(dir, displayName));
        }
        catch
        {
            kits.Add(new Kit(dir, dir));
        }
    }

    return kits;
}

static string? ParseDisplayName(string yaml)
{
    // Try displayName first, fall back to name
    var displayMatch = Regex.Match(yaml, @"^displayName:\s*(.+)$", RegexOptions.Multiline);
    if (displayMatch.Success)
        return displayMatch.Groups[1].Value.Trim().Trim('"');

    var nameMatch = Regex.Match(yaml, @"^name:\s*(.+)$", RegexOptions.Multiline);
    if (nameMatch.Success)
        return nameMatch.Groups[1].Value.Trim().Trim('"');

    return null;
}

record Kit(string Directory, string DisplayName);
