#:package Spectre.Console@0.57.0

using System.Diagnostics;
using System.Text.RegularExpressions;
using Spectre.Console;

var defaultName = new DirectoryInfo(Directory.GetCurrentDirectory()).Name;
var name = AnsiConsole.Prompt(
    new TextPrompt<string>("Enter the [green]sandbox name[/]:")
        .DefaultValue(defaultName));

var workDir = AnsiConsole.Prompt(
    new TextPrompt<string>("Enter the [green]working directory[/]:")
        .DefaultValue("."));

var workspaceMode = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("Select [green]workspace mode[/]:")
        .AddChoices("Direct", "Clone"));

var kitFlags = "";

if (AnsiConsole.Confirm("Do you want to add any kits?"))
{
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
        .StartAsync("Fetching kits...", async ctx =>
        {
            var cloneDir = await EnsureRepo(owner, repo, ctx);
            kits = FindKits(cloneDir);
        });

    if (kits.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No kits found in the repository.[/]");
    }
    else
    {
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

        if (selected.Count > 0)
        {
            var gitUrl = $"git+https://github.com/{owner}/{repo}.git";
            kitFlags = string.Join(" ", selected.Select(k =>
                k.Directory is not null
                    ? $"--kit \"{gitUrl}#dir={k.Directory}\""
                    : $"--kit \"{gitUrl}\""));
        }
    }
}

var commandParts = new List<string> { "sbx run", $"--name \"{name}\"" };
if (!string.IsNullOrEmpty(kitFlags)) commandParts.Add(kitFlags);
if (workspaceMode == "Clone") commandParts.Add("--clone");
commandParts.Add("claude");
commandParts.Add($"\"{workDir}\"");
var command = string.Join(" ", commandParts);

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

static async Task<string> EnsureRepo(string owner, string repo, StatusContext ctx)
{
    var cloneDir = Path.Combine(Path.GetTempPath(), "create-sbx", owner, repo);

    if (Directory.Exists(Path.Combine(cloneDir, ".git")))
    {
        ctx.Status("Fetching latest kits...");
        await RunProcess("git", ["fetch", "--depth=1", "origin"], cloneDir);
        await RunProcess("git", ["reset", "--hard", "FETCH_HEAD"], cloneDir);
    }
    else
    {
        ctx.Status("Cloning repository...");
        Directory.CreateDirectory(Path.GetDirectoryName(cloneDir)!);
        await RunProcess("git", ["clone", "--depth=1", $"https://github.com/{owner}/{repo}.git", cloneDir]);
    }

    return cloneDir;
}

static List<Kit> FindKits(string cloneDir)
{
    // If the root contains spec.yaml, the whole repo is a single kit
    var rootSpec = Path.Combine(cloneDir, "spec.yaml");
    if (File.Exists(rootSpec))
    {
        var specYaml = File.ReadAllText(rootSpec);
        var displayName = ParseDisplayName(specYaml) ?? Path.GetFileName(cloneDir);
        return [new Kit(null, displayName!)];
    }

    // Otherwise look for kits in top-level subdirectories
    var kits = new List<Kit>();
    foreach (var dir in Directory.GetDirectories(cloneDir).Order())
    {
        var dirName = Path.GetFileName(dir)!;
        if (dirName.StartsWith('.')) continue;

        var specFile = Path.Combine(dir, "spec.yaml");
        if (!File.Exists(specFile)) continue;

        var specYaml = File.ReadAllText(specFile);
        var displayName = ParseDisplayName(specYaml) ?? dirName;
        kits.Add(new Kit(dirName, displayName));
    }
    return kits;
}

static async Task RunProcess(string fileName, string[] args, string? workDir = null)
{
    var psi = new ProcessStartInfo(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    if (workDir != null)
        psi.WorkingDirectory = workDir;
    foreach (var arg in args)
        psi.ArgumentList.Add(arg);

    using var process = Process.Start(psi)!;
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
        throw new Exception((await errorTask).Trim());
}

static string? ParseDisplayName(string yaml)
{
    var displayMatch = Regex.Match(yaml, @"^displayName:\s*(.+)$", RegexOptions.Multiline);
    if (displayMatch.Success)
        return displayMatch.Groups[1].Value.Trim().Trim('"');

    var nameMatch = Regex.Match(yaml, @"^name:\s*(.+)$", RegexOptions.Multiline);
    if (nameMatch.Success)
        return nameMatch.Groups[1].Value.Trim().Trim('"');

    return null;
}

record Kit(string? Directory, string DisplayName);
