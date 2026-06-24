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
        .UseConverter(k => k.Name));

if (selected.Count == 0)
{
    AnsiConsole.MarkupLine("[yellow]No kits selected.[/]");
    return 0;
}

var kitArgs = string.Join(" ", selected.Select(k => $"--kit \"{repoUrl}/tree/main/{k.Path}\""));
var command = $"sbx create {kitArgs}";

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

    var url = $"https://api.github.com/repos/{owner}/{repo}/git/trees/main?recursive=1";
    var response = await http.GetStringAsync(url);

    using var doc = JsonDocument.Parse(response);
    var tree = doc.RootElement.GetProperty("tree");

    var kits = new List<Kit>();
    foreach (var item in tree.EnumerateArray())
    {
        if (item.GetProperty("type").GetString() != "blob") continue;
        var path = item.GetProperty("path").GetString()!;

        if (Path.GetFileName(path).Equals("kit.yaml", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path).Equals("kit.yml", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path).Equals("kit.json", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
            var name = string.IsNullOrEmpty(dir) ? repo : Path.GetFileName(dir);
            kits.Add(new Kit(name, dir));
        }
    }

    return kits;
}

record Kit(string Name, string Path);
