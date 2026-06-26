#!/usr/bin/env -S dotnet run
#:package Spectre.Console@0.57.0

using System.Diagnostics;
using System.Text.RegularExpressions;
using Spectre.Console;

var defaultName = new DirectoryInfo(Directory.GetCurrentDirectory()).Name;
var name = AnsiConsole.Prompt(
    new TextPrompt<string>("Enter the [green]sandbox name[/]:")
        .DefaultValue(defaultName));

var builtInAgents = new List<AgentOption>
{
    new("claude", "Claude Code", null),
    new("codex", "Codex", null),
    new("copilot", "Copilot", null),
    new("cursor", "Cursor", null),
    new("droid", "Droid", null),
    new("gemini", "Gemini", null),
    new("kiro", "Kiro", null),
    new("opencode", "OpenCode", null),
    new("docker-agent", "Docker Agent", null),
    new("shell", "Shell", "Agent-less sandbox for manual setup or testing"),
};

const string CustomAgentSentinel = "__custom__";

var selectedAgent = AnsiConsole.Prompt(
    new SelectionPrompt<AgentOption>()
        .Title("Select [green]agent[/]:")
        .AddChoices([.. builtInAgents, new AgentOption(CustomAgentSentinel, "Custom agent...", null)])
        .UseConverter(a => a.Id == CustomAgentSentinel
            ? "[grey]Custom agent...[/]"
            : a.Description is not null
                ? $"{a.DisplayName} [grey]({a.Id})[/] [grey]- {a.Description}[/]"
                : $"{a.DisplayName} [grey]({a.Id})[/]"));

var agentId = selectedAgent.Id == CustomAgentSentinel
    ? AnsiConsole.Ask<string>("Enter the [green]custom agent identifier[/]:")
    : selectedAgent.Id;

AnsiConsole.MarkupLine($"Agent: [cyan]{Markup.Escape(agentId)}[/]");

var workDir = AnsiConsole.Prompt(
    new TextPrompt<string>("Enter the [green]working directory[/]:")
        .DefaultValue("."));

var workspaceMode = AnsiConsole.Prompt(
    new SelectionPrompt<WorkspaceMode>()
        .Title("Select [green]workspace mode[/]:")
        .AddChoices(
            new WorkspaceMode("Direct", "Mount the host directory directly into the sandbox", false),
            new WorkspaceMode("Clone", "Clone the repository into the sandbox", true))
        .UseConverter(m => $"{m.Name} [grey]- {m.Description}[/]"));

AnsiConsole.MarkupLine($"Workspace mode: [cyan]{workspaceMode.Name}[/]");

var allKitUrls = new List<string>();

if (AnsiConsole.Confirm("Add a kit?"))
{
    var recentUrls = LoadRecentUrls();

    do
    {
        var repoUrl = PromptForUrl(recentUrls);

        var (owner, repo) = ParseGitHubUrl(repoUrl);
        if (owner is null || repo is null)
        {
            AnsiConsole.MarkupLine("[red]Invalid GitHub repository URL. Expected format: https://github.com/owner/repo[/]");
            break;
        }

        recentUrls = [repoUrl, .. recentUrls.Where(u => u != repoUrl).Take(9)];
        SaveRecentUrls(recentUrls);

        var branch = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter [green]branch[/] (leave blank for default):")
                .AllowEmpty());

        List<Kit> kits = [];
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Fetching kits...", async ctx =>
            {
                var cloneDir = await EnsureRepo(owner, repo, branch, ctx);
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
                var branchLabel = string.IsNullOrEmpty(branch) ? "" : $" [grey]({Markup.Escape(branch)})[/]";
                AnsiConsole.MarkupLine($"Selected kits from [cyan]{Markup.Escape(repoUrl)}[/]{branchLabel}: {Markup.Escape(string.Join(", ", selected.Select(k => k.DisplayName)))}");
                var gitUrl = $"git+https://github.com/{owner}/{repo}.git";
                var refFragment = string.IsNullOrEmpty(branch) ? "" : $"&ref={Uri.EscapeDataString(branch)}";
                foreach (var k in selected)
                    allKitUrls.Add(k.Directory is not null
                        ? $"{gitUrl}#dir={k.Directory}{refFragment}"
                        : string.IsNullOrEmpty(refFragment) ? gitUrl : $"{gitUrl}#{refFragment.TrimStart('&')}");
            }
        }
    } while (AnsiConsole.Confirm("Add another kit?"));
}

var commandParts = new List<string> { "sbx create", $"--name \"{name}\"" };
if (allKitUrls.Count > 0) commandParts.Add(string.Join(" ", allKitUrls.Select(u => $"--kit \"{u}\"")));
if (workspaceMode.UseClone) commandParts.Add("--clone");
commandParts.Add(agentId);
commandParts.Add($"\"{workDir}\"");
var command = string.Join(" ", commandParts);

var sbxArgs = new List<string> { "create", "--name", name };
foreach (var kitUrl in allKitUrls) { sbxArgs.Add("--kit"); sbxArgs.Add(kitUrl); }
if (workspaceMode.UseClone) sbxArgs.Add("--clone");
sbxArgs.Add(agentId);
sbxArgs.Add(workDir);

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[bold]Command:[/]");
AnsiConsole.MarkupLine($"[blue]{Markup.Escape(command)}[/]");
AnsiConsole.WriteLine();

if (AnsiConsole.Confirm("Create the sandbox?"))
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"Creating sandbox [cyan]{Markup.Escape(name)}[/]...");
    AnsiConsole.WriteLine();
    var psi = new ProcessStartInfo("sbx") { UseShellExecute = false };
    foreach (var arg in sbxArgs)
        psi.ArgumentList.Add(arg);
    using var proc = Process.Start(psi)!;
    await proc.WaitForExitAsync();
    if (proc.ExitCode != 0)
    {
        AnsiConsole.MarkupLine($"[red]sbx exited with code {proc.ExitCode}[/]");
        return proc.ExitCode;
    }
}

return 0;

static string PromptForUrl(List<string> recentUrls)
{
    const string NewUrlOption = "Enter kit URL";

    if (recentUrls.Count > 0)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a [green]kit repository URL[/]:")
                .AddChoices([.. recentUrls, NewUrlOption]));

        if (choice != NewUrlOption)
            return choice;
    }

    var url = AnsiConsole.Ask<string>("Enter the [green]GitHub repository URL[/] containing sbx kits:");
    return url.Trim().TrimEnd('/');
}

static string GetRecentUrlsPath() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "create-sbx", "recent-urls.txt");

static List<string> LoadRecentUrls()
{
    var path = GetRecentUrlsPath();
    if (!File.Exists(path)) return [];
    return [.. File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l))];
}

static void SaveRecentUrls(List<string> urls)
{
    var path = GetRecentUrlsPath();
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllLines(path, urls);
}

static (string? owner, string? repo) ParseGitHubUrl(string url)
{
    var match = Regex.Match(url, @"github\.com[/:](?<owner>[^/]+)/(?<repo>[^/.]+)");
    if (!match.Success) return (null, null);
    return (match.Groups["owner"].Value, match.Groups["repo"].Value);
}

static async Task<string> EnsureRepo(string owner, string repo, string branch, StatusContext ctx)
{
    var cloneDir = Path.Combine(Path.GetTempPath(), "create-sbx", owner, repo);

    if (Directory.Exists(Path.Combine(cloneDir, ".git")))
    {
        ctx.Status("Fetching latest kits...");
        if (string.IsNullOrEmpty(branch))
            await RunProcess("git", ["fetch", "origin"], cloneDir);
        else
            await RunProcess("git", ["fetch", "origin", $"+refs/heads/{branch}:refs/remotes/origin/{branch}"], cloneDir);
    }
    else
    {
        ctx.Status("Cloning repository...");
        Directory.CreateDirectory(Path.GetDirectoryName(cloneDir)!);
        await RunProcess("git", ["clone", $"https://github.com/{owner}/{repo}.git", cloneDir]);
    }

    if (string.IsNullOrEmpty(branch))
        await RunProcess("git", ["checkout", "--detach", "origin/HEAD"], cloneDir);
    else
        await RunProcess("git", ["checkout", "--detach", $"origin/{branch}"], cloneDir);

    return cloneDir;
}

static List<Kit> FindKits(string cloneDir)
{
    var kits = new List<Kit>();

    var rootSpec = Path.Combine(cloneDir, "spec.yaml");
    if (File.Exists(rootSpec))
    {
        var specYaml = File.ReadAllText(rootSpec);
        var displayName = ParseDisplayName(specYaml) ?? Path.GetFileName(cloneDir);
        kits.Add(new Kit(null, displayName!));
    }

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

record AgentOption(string Id, string DisplayName, string? Description);
record Kit(string? Directory, string DisplayName);
record WorkspaceMode(string Name, string Description, bool UseClone);
