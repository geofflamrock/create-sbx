#!/usr/bin/env -S dotnet run
#:package Spectre.Console@0.57.0
#:package System.CommandLine@2.0.9

using System.CommandLine;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Spectre.Console;

var rootCommand = new RootCommand("An interactive CLI for creating Docker Sandboxes using `sbx`");
rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
    await RunAsync());
return await rootCommand.Parse(args).InvokeAsync();

async Task<int> RunAsync()
{
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

    var recentUrls = LoadRecentUrls();
    var fetchedRepos = new HashSet<string>();

    // Template selection
    TemplateConfig? template = null;
    if (AnsiConsole.Confirm("Use a custom template?", false))
    {
        var templateSources = new[]
        {
            new TemplateSourceOption(TemplateSource.Registry, "Docker image"),
            new TemplateSourceOption(TemplateSource.GitRepo, "Dockerfile - Git repository"),
            new TemplateSourceOption(TemplateSource.Local, "Dockerfile - local"),
        };

        var selectedSource = AnsiConsole.Prompt(
            new SelectionPrompt<TemplateSourceOption>()
                .Title("Select [green]template source[/]:")
                .AddChoices(templateSources)
                .UseConverter(s => s.DisplayName));

        if (selectedSource.Source == TemplateSource.Registry)
        {
            var imageName = AnsiConsole.Ask<string>("Enter the [green]image name[/] [grey](e.g. ubuntu:22.04)[/]:");
            imageName = imageName.Trim();
            template = new TemplateConfig(TemplateSource.Registry, imageName, null, null);
            AnsiConsole.MarkupLine($"Template: [cyan]{Markup.Escape(imageName)}[/]");
        }
        else if (selectedSource.Source == TemplateSource.GitRepo)
        {
            var repoUrl = PromptForUrl(recentUrls, "template");
            var (tOwner, tRepo) = ParseGitHubUrl(repoUrl);
            if (tOwner is null || tRepo is null)
            {
                AnsiConsole.MarkupLine("[red]Invalid GitHub repository URL. Expected format: https://github.com/owner/repo[/]");
            }
            else
            {
                recentUrls = [repoUrl, .. recentUrls.Where(u => u != repoUrl).Take(9)];
                SaveRecentUrls(recentUrls);

                var tBranch = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter [green]branch[/] (leave blank for default):")
                        .AllowEmpty());

                List<string> dockerfiles = [];
                string cloneDir = "";
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Fetching repository...", async ctx =>
                    {
                        cloneDir = await EnsureRepo(tOwner, tRepo, tBranch, ctx, fetchedRepos);
                        dockerfiles = FindDockerfiles(cloneDir);
                    });

                if (dockerfiles.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No Dockerfiles found in the repository.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[green]Found {dockerfiles.Count} Dockerfile(s).[/]");

                    var selectedDockerfile = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Select a [green]Dockerfile[/]:")
                            .PageSize(20)
                            .AddChoices(dockerfiles));

                    var absolutePath = Path.Combine(cloneDir, selectedDockerfile);
                    var imageName = $"create-sbx-{Guid.NewGuid().ToString("N")[..8]}";
                    template = new TemplateConfig(TemplateSource.GitRepo, imageName, absolutePath, cloneDir);

                    var branchLabel = string.IsNullOrEmpty(tBranch) ? "" : $" [grey]({Markup.Escape(tBranch)})[/]";
                    AnsiConsole.MarkupLine($"Template: [cyan]{Markup.Escape(selectedDockerfile)}[/] from [cyan]{Markup.Escape(repoUrl)}[/]{branchLabel}");
                    AnsiConsole.MarkupLine("[yellow]Note: The Dockerfile will be built before creating the sandbox.[/]");
                }
            }
        }
        else // Local Dockerfile
        {
            var dockerfilePath = AnsiConsole.Ask<string>("Enter the [green]path to the Dockerfile[/]:");
            dockerfilePath = Path.GetFullPath(dockerfilePath.Trim());

            if (!File.Exists(dockerfilePath))
            {
                AnsiConsole.MarkupLine($"[red]Dockerfile not found: {Markup.Escape(dockerfilePath)}[/]");
            }
            else
            {
                var context = Path.GetDirectoryName(dockerfilePath)!;
                var imageName = $"create-sbx-{Guid.NewGuid().ToString("N")[..8]}";
                template = new TemplateConfig(TemplateSource.Local, imageName, dockerfilePath, context);
                AnsiConsole.MarkupLine($"Template: [cyan]{Markup.Escape(dockerfilePath)}[/]");
                AnsiConsole.MarkupLine("[yellow]Note: The Dockerfile will be built before creating the sandbox.[/]");
            }
        }
    }

    // Kit selection
    var allKitUrls = new List<string>();
    if (AnsiConsole.Confirm("Add a kit?"))
    {
        do
        {
            var repoUrl = PromptForUrl(recentUrls, "kit");

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
                    var cloneDir = await EnsureRepo(owner, repo, branch, ctx, fetchedRepos);
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
                        .UseConverter(k => k.Description is not null
                            ? $"{k.DisplayName} [grey]- {Markup.Escape(k.Description)}[/]"
                            : k.DisplayName));

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
    if (template is not null)
    {
        var displayTemplateName = template.Source is TemplateSource.GitRepo or TemplateSource.Local
            ? "<image-id>"
            : template.ImageName;
        commandParts.Add($"--template \"{displayTemplateName}\"");
    }
    if (allKitUrls.Count > 0) commandParts.Add(string.Join(" ", allKitUrls.Select(u => $"--kit \"{u}\"")));
    if (workspaceMode.UseClone) commandParts.Add("--clone");
    commandParts.Add(agentId);
    commandParts.Add($"\"{workDir}\"");
    var command = string.Join(" ", commandParts);

    AnsiConsole.WriteLine();
    if (template?.Source is TemplateSource.GitRepo or TemplateSource.Local)
    {
        AnsiConsole.MarkupLine($"[yellow]The Dockerfile [cyan]{Markup.Escape(template.DockerfilePath!)}[/] will be built before creating the sandbox.[/]");
        AnsiConsole.WriteLine();
    }
    AnsiConsole.MarkupLine("[bold]Command:[/]");
    AnsiConsole.MarkupLine($"[blue]{Markup.Escape(command)}[/]");
    AnsiConsole.WriteLine();

    if (AnsiConsole.Confirm("Create the sandbox?"))
    {
        string? effectiveTemplateName = template?.Source == TemplateSource.Registry
            ? template.ImageName
            : null;

        if (template?.Source is TemplateSource.GitRepo or TemplateSource.Local)
        {
            try
            {
                effectiveTemplateName = await BuildAndLoadDockerImage(template);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to build Docker image: {Markup.Escape(ex.Message)}[/]");
                return 1;
            }
        }

        var sbxArgs = new List<string> { "create", "--name", name };
        if (effectiveTemplateName is not null) { sbxArgs.Add("--template"); sbxArgs.Add(effectiveTemplateName); }
        foreach (var kitUrl in allKitUrls) { sbxArgs.Add("--kit"); sbxArgs.Add(kitUrl); }
        if (workspaceMode.UseClone) sbxArgs.Add("--clone");
        sbxArgs.Add(agentId);
        sbxArgs.Add(workDir);

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
}

static string PromptForUrl(List<string> recentUrls, string purpose = "kit")
{
    const string NewUrlOption = "Enter URL";

    if (recentUrls.Count > 0)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Select a [green]{purpose} repository URL[/]:")
                .AddChoices([.. recentUrls, NewUrlOption]));

        if (choice != NewUrlOption)
            return choice;
    }

    var url = AnsiConsole.Ask<string>($"Enter the [green]GitHub repository URL[/] for the {purpose}:");
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

static async Task<string> EnsureRepo(string owner, string repo, string branch, StatusContext ctx, HashSet<string>? fetchedRepos = null)
{
    var cloneDir = Path.Combine(Path.GetTempPath(), "create-sbx", owner, repo);
    var repoKey = $"{owner}/{repo}#{branch}";

    if (fetchedRepos?.Contains(repoKey) == true)
        return cloneDir;

    if (Directory.Exists(Path.Combine(cloneDir, ".git")))
    {
        ctx.Status("Fetching latest changes...");
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

    fetchedRepos?.Add(repoKey);
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
        var description = ParseDescription(specYaml);
        kits.Add(new Kit(null, displayName!, description));
    }

    foreach (var dir in Directory.GetDirectories(cloneDir).Order())
    {
        var dirName = Path.GetFileName(dir)!;
        if (dirName.StartsWith('.')) continue;

        var specFile = Path.Combine(dir, "spec.yaml");
        if (!File.Exists(specFile)) continue;

        var specYaml = File.ReadAllText(specFile);
        var displayName = ParseDisplayName(specYaml) ?? dirName;
        var description = ParseDescription(specYaml);
        kits.Add(new Kit(dirName, displayName, description));
    }
    return kits;
}

static List<string> FindDockerfiles(string repoDir)
{
    var results = new List<string>();
    FindDockerfilesRecursive(repoDir, repoDir, results);
    results.Sort();
    return results;
}

static void FindDockerfilesRecursive(string baseDir, string currentDir, List<string> results)
{
    foreach (var file in Directory.GetFiles(currentDir))
    {
        var fileName = Path.GetFileName(file);
        if (IsDockerfileName(fileName))
            results.Add(Path.GetRelativePath(baseDir, file));
    }

    foreach (var dir in Directory.GetDirectories(currentDir))
    {
        if (Path.GetFileName(dir)!.StartsWith('.')) continue;
        FindDockerfilesRecursive(baseDir, dir, results);
    }
}

static bool IsDockerfileName(string fileName) =>
    fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
    fileName.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase) ||
    fileName.EndsWith(".dockerfile", StringComparison.OrdinalIgnoreCase);

static async Task<string> BuildAndLoadDockerImage(TemplateConfig template)
{
    var imagesDir = Path.Combine(Path.GetTempPath(), "create-sbx", "images");
    Directory.CreateDirectory(imagesDir);

    AnsiConsole.MarkupLine($"Building Dockerfile [cyan]{Markup.Escape(template.DockerfilePath!)}[/]...");
    await RunProcessInteractive("docker", [
        "build",
        "-t", template.ImageName,
        "-f", template.DockerfilePath!,
        template.DockerContext!
    ]);

    var imageId = await GetDockerImageId(template.ImageName);
    var shortHash = imageId.StartsWith("sha256:") ? imageId[7..19] : imageId[..12];
    var stableImageName = $"create-sbx-{shortHash}";
    var tarPath = Path.Combine(imagesDir, $"{stableImageName}.tar");

    if (!File.Exists(tarPath))
    {
        await RunProcess("docker", ["tag", template.ImageName, stableImageName]);

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Saving Docker image...", async ctx =>
            {
                await RunProcess("docker", ["save", stableImageName, "-o", tarPath]);
            });

        AnsiConsole.MarkupLine($"[green]Docker image built successfully.[/]");

        AnsiConsole.MarkupLine("Loading template into sbx...");
        await RunProcessInteractive("sbx", ["template", "load", tarPath]);
        AnsiConsole.MarkupLine($"[green]Template loaded into sbx.[/]");
    }
    else
    {
        AnsiConsole.MarkupLine($"[green]Docker image built. Using cached template [cyan]{stableImageName}[/].[/]");
    }

    return stableImageName;
}

static async Task<string> GetDockerImageId(string imageName)
{
    var psi = new ProcessStartInfo("docker")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    psi.ArgumentList.Add("inspect");
    psi.ArgumentList.Add("--format={{.Id}}");
    psi.ArgumentList.Add(imageName);

    using var process = Process.Start(psi)!;
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
        throw new Exception((await errorTask).Trim());

    return (await outputTask).Trim();
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

static async Task RunProcessInteractive(string fileName, string[] args, string? workDir = null)
{
    var psi = new ProcessStartInfo(fileName) { UseShellExecute = false };
    if (workDir != null)
        psi.WorkingDirectory = workDir;
    foreach (var arg in args)
        psi.ArgumentList.Add(arg);

    using var process = Process.Start(psi)!;
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
        throw new Exception($"{fileName} exited with code {process.ExitCode}");
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

static string? ParseDescription(string yaml)
{
    var match = Regex.Match(yaml, @"^description:\s*(.+)$", RegexOptions.Multiline);
    if (match.Success)
        return match.Groups[1].Value.Trim().Trim('"');
    return null;
}

record AgentOption(string Id, string DisplayName, string? Description);
record Kit(string? Directory, string DisplayName, string? Description);
record WorkspaceMode(string Name, string Description, bool UseClone);
record TemplateSourceOption(TemplateSource Source, string DisplayName);
record TemplateConfig(TemplateSource Source, string ImageName, string? DockerfilePath, string? DockerContext);
enum TemplateSource { Registry, GitRepo, Local }
