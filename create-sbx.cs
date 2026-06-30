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
    var workspaceFolderName = new DirectoryInfo(Directory.GetCurrentDirectory()).Name;

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

    var config = new SandboxConfig
    {
        Name = workspaceFolderName,
        SelectedAgent = builtInAgents[0],
        WorkingDirectory = ".",
        WorkspaceMode = new WorkspaceMode("Direct", "Mount the host directory directly into the sandbox", false),
        Template = null,
        Kits = [],
    };

    var tuiState = new TuiState
    {
        Config = config,
        FocusIndex = 0,
        RecentUrls = LoadRecentUrls(),
        FetchedRepos = [],
    };

    var tuiResult = await RunTuiAsync(tuiState, workspaceFolderName, builtInAgents);
    if (tuiResult != 0) return 0;

    var agentId = config.CustomAgentId ?? config.SelectedAgent.Id;
    var kitUrls = config.Kits.Select(k => k.Url).ToList();
    var displayTemplateName = config.Template?.Source is TemplateSource.GitRepo or TemplateSource.Local
        ? "<image-id>"
        : config.Template?.ImageName;

    AnsiConsole.WriteLine();
    if (config.Template?.Source is TemplateSource.GitRepo or TemplateSource.Local)
    {
        AnsiConsole.MarkupLine($"[yellow]The Dockerfile [cyan]{Markup.Escape(config.Template.DockerfilePath!)}[/] will be built before creating the sandbox.[/]");
        AnsiConsole.WriteLine();
    }
    PrintSbxCommand(config.Name, displayTemplateName, kitUrls, config.WorkspaceMode, agentId, config.WorkingDirectory);
    AnsiConsole.WriteLine();

    string? effectiveTemplateName = config.Template?.Source == TemplateSource.Registry
        ? config.Template.ImageName
        : null;

    if (config.Template?.Source is TemplateSource.GitRepo or TemplateSource.Local)
    {
        try
        {
            effectiveTemplateName = await BuildAndLoadDockerImage(config.Template);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to build Docker image: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    var sbxArgs = new List<string> { "create", "--name", config.Name };
    if (effectiveTemplateName is not null) { sbxArgs.Add("--template"); sbxArgs.Add(effectiveTemplateName); }
    foreach (var kitUrl in kitUrls) { sbxArgs.Add("--kit"); sbxArgs.Add(kitUrl); }
    if (config.WorkspaceMode.UseClone) sbxArgs.Add("--clone");
    sbxArgs.Add(agentId);
    sbxArgs.Add(config.WorkingDirectory);

    if (config.Template?.Source is TemplateSource.GitRepo or TemplateSource.Local)
    {
        AnsiConsole.WriteLine();
        PrintSbxCommand(config.Name, effectiveTemplateName, kitUrls, config.WorkspaceMode, agentId, config.WorkingDirectory);
    }

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"Creating sandbox [cyan]{Markup.Escape(config.Name)}[/]...");
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

    return 0;
}

async Task<int> RunTuiAsync(TuiState state, string workspaceFolderName, List<AgentOption> builtInAgents)
{
    const string CustomAgentSentinel = "__custom__";

    var workspaceModes = new List<WorkspaceMode>
    {
        new("Direct", "Mount the host directory directly into the sandbox", false),
        new("Clone", "Clone the repository into the sandbox", true),
    };

    async Task DispatchActionAsync(string action)
    {
        switch (action)
        {
            case "edit_name":
            {
                state.Config.Name = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the [green]sandbox name[/]:")
                        .DefaultValue(state.Config.Name));
                break;
            }
            case "edit_agent":
            {
                var selected = AnsiConsole.Prompt(
                    new SelectionPrompt<AgentOption>()
                        .Title("Select [green]agent[/]:")
                        .AddChoices([.. builtInAgents, new AgentOption(CustomAgentSentinel, "Custom agent...", null)])
                        .UseConverter(a => a.Id == CustomAgentSentinel
                            ? "[grey]Custom agent...[/]"
                            : a.Description is not null
                                ? $"{a.DisplayName} [grey]({a.Id})[/] [grey]- {a.Description}[/]"
                                : $"{a.DisplayName} [grey]({a.Id})[/]"));

                if (selected.Id == CustomAgentSentinel)
                {
                    var customId = AnsiConsole.Ask<string>("Enter the [green]custom agent identifier[/]:");
                    state.Config.SelectedAgent = new AgentOption(customId, customId, null);
                    state.Config.CustomAgentId = customId;
                }
                else
                {
                    state.Config.SelectedAgent = selected;
                    state.Config.CustomAgentId = null;
                }
                break;
            }
            case "edit_workdir":
            {
                state.Config.WorkingDirectory = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the [green]working directory[/]:")
                        .DefaultValue(state.Config.WorkingDirectory));
                break;
            }
            case "edit_workspace_mode":
            {
                state.Config.WorkspaceMode = AnsiConsole.Prompt(
                    new SelectionPrompt<WorkspaceMode>()
                        .Title("Select [green]workspace mode[/]:")
                        .AddChoices(workspaceModes)
                        .UseConverter(m => $"{m.Name} [grey]- {m.Description}[/]"));
                break;
            }
            case "edit_template":
            {
                var templateChoices = new (int Key, string Label)[]
                {
                    (-1, "None (use default)"),
                    ((int)TemplateSource.Registry, "Docker image"),
                    ((int)TemplateSource.GitRepo, "Dockerfile - Git repository"),
                    ((int)TemplateSource.Local, "Dockerfile - local"),
                };

                var currentTitle = state.Config.Template is null
                    ? "Select [green]template source[/]:"
                    : $"Current: [cyan]{Markup.Escape(GetTemplateDisplay(state.Config.Template))}[/]. Select new source:";

                var selectedChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<(int Key, string Label)>()
                        .Title(currentTitle)
                        .AddChoices(templateChoices)
                        .UseConverter(c => c.Label));

                if (selectedChoice.Key == -1)
                {
                    state.Config.Template = null;
                    break;
                }

                var source = (TemplateSource)selectedChoice.Key;

                if (source == TemplateSource.Registry)
                {
                    var imageName = AnsiConsole.Ask<string>("Enter the [green]image name[/] [grey](e.g. ubuntu:22.04)[/]:");
                    state.Config.Template = new TemplateConfig(TemplateSource.Registry, imageName.Trim(), null, null);
                    break;
                }

                if (source == TemplateSource.GitRepo)
                {
                    var repoUrl = PromptForUrl(state.RecentUrls, "template");
                    var (tOwner, tRepo) = ParseGitHubUrl(repoUrl);
                    if (tOwner is null || tRepo is null)
                    {
                        AnsiConsole.MarkupLine("[red]Invalid GitHub repository URL. Expected format: https://github.com/owner/repo[/]");
                        break;
                    }

                    AddRecentUrl(state.RecentUrls, repoUrl);

                    var tBranch = AnsiConsole.Prompt(
                        new TextPrompt<string>("Enter [green]branch[/] (leave blank for default):")
                            .AllowEmpty());

                    List<string> dockerfiles = [];
                    string cloneDir = "";
                    await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .StartAsync("Fetching repository...", async ctx =>
                        {
                            cloneDir = await EnsureRepo(tOwner, tRepo, tBranch, ctx, state.FetchedRepos);
                            dockerfiles = FindDockerfiles(cloneDir);
                        });

                    if (dockerfiles.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[yellow]No Dockerfiles found in the repository.[/]");
                        break;
                    }

                    var selectedDockerfile = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Select a [green]Dockerfile[/]:")
                            .PageSize(20)
                            .AddChoices(dockerfiles));

                    var absolutePath = Path.Combine(cloneDir, selectedDockerfile);
                    var imageName = $"create-sbx-{Guid.NewGuid().ToString("N")[..8]}";
                    state.Config.Template = new TemplateConfig(TemplateSource.GitRepo, imageName, absolutePath, cloneDir, tBranch);
                    break;
                }

                // Local Dockerfile
                {
                    var dockerfilePath = AnsiConsole.Ask<string>("Enter the [green]path to the Dockerfile[/]:");
                    dockerfilePath = Path.GetFullPath(dockerfilePath.Trim());

                    if (!File.Exists(dockerfilePath))
                    {
                        AnsiConsole.MarkupLine($"[red]Dockerfile not found: {Markup.Escape(dockerfilePath)}[/]");
                        break;
                    }

                    var context = Path.GetDirectoryName(dockerfilePath)!;
                    var imageName = $"create-sbx-{Guid.NewGuid().ToString("N")[..8]}";
                    state.Config.Template = new TemplateConfig(TemplateSource.Local, imageName, dockerfilePath, context);
                }
                break;
            }
            case "add_kit":
            {
                var repoUrl = PromptForUrl(state.RecentUrls, "kit");
                var (owner, repo) = ParseGitHubUrl(repoUrl);
                if (owner is null || repo is null)
                {
                    AnsiConsole.MarkupLine("[red]Invalid GitHub repository URL. Expected format: https://github.com/owner/repo[/]");
                    break;
                }

                AddRecentUrl(state.RecentUrls, repoUrl);

                var branch = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter [green]branch[/] (leave blank for default):")
                        .AllowEmpty());

                List<Kit> kits = [];
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Fetching kits...", async ctx =>
                    {
                        var cloneDir = await EnsureRepo(owner, repo, branch, ctx, state.FetchedRepos);
                        kits = FindKits(cloneDir);
                    });

                if (kits.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No kits found in the repository.[/]");
                    break;
                }

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
                    var gitUrl = $"git+https://github.com/{owner}/{repo}.git";
                    var refFragment = string.IsNullOrEmpty(branch) ? "" : $"&ref={Uri.EscapeDataString(branch)}";
                    foreach (var k in selected)
                    {
                        var url = k.Directory is not null
                            ? $"{gitUrl}#dir={k.Directory}{refFragment}"
                            : string.IsNullOrEmpty(refFragment) ? gitUrl : $"{gitUrl}#{refFragment.TrimStart('&')}";
                        state.Config.Kits.Add(new KitEntry(url, k.DisplayName));
                    }
                }
                break;
            }
            default:
            {
                if (action.StartsWith("kit:"))
                {
                    var idx = int.Parse(action.Split(':')[1]);
                    if (idx < state.Config.Kits.Count)
                    {
                        if (AnsiConsole.Confirm($"Remove kit [cyan]{Markup.Escape(state.Config.Kits[idx].DisplayName)}[/]?"))
                        {
                            state.Config.Kits.RemoveAt(idx);
                            var newItems = GetFocusableItems(state);
                            state.FocusIndex = Math.Min(state.FocusIndex, newItems.Count - 1);
                        }
                    }
                }
                else if (action.StartsWith("remove_kit:"))
                {
                    var idx = int.Parse(action.Split(':')[1]);
                    if (idx < state.Config.Kits.Count)
                    {
                        state.Config.Kits.RemoveAt(idx);
                        var newItems = GetFocusableItems(state);
                        state.FocusIndex = Math.Min(state.FocusIndex, newItems.Count - 1);
                    }
                }
                break;
            }
        }
    }

    while (true)
    {
        AnsiConsole.Clear();
        await AnsiConsole.Live(RenderForm(state, workspaceFolderName))
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                ctx.UpdateTarget(RenderForm(state, workspaceFolderName));
                while (state.PendingAction is null)
                {
                    var key = Console.ReadKey(intercept: true);
                    HandleKeyPress(key, state, workspaceFolderName);
                    ctx.UpdateTarget(RenderForm(state, workspaceFolderName));
                }
            });

        var action = state.PendingAction!;
        state.PendingAction = null;

        if (action == "cancel") return -1;
        if (action == "create") return 0;

        await DispatchActionAsync(action);
    }
}

static List<string> GetFocusableItems(TuiState state)
{
    var items = new List<string> { "name", "agent", "workdir", "workspace_mode", "template" };
    for (var i = 0; i < state.Config.Kits.Count; i++)
        items.Add($"kit:{i}");
    items.Add("add_kit");
    items.Add("create");
    items.Add("cancel");
    return items;
}

static IRenderable RenderForm(TuiState state, string workspaceFolderName)
{
    var config = state.Config;
    var items = GetFocusableItems(state);
    var focused = items[Math.Clamp(state.FocusIndex, 0, items.Count - 1)];

    var agentDisplay = config.CustomAgentId is not null
        ? $"Custom: {config.CustomAgentId}"
        : $"{config.SelectedAgent.DisplayName} ({config.SelectedAgent.Id})";
    var agentIsDefault = config.SelectedAgent.Id == "claude" && config.CustomAgentId is null;
    var templateDisplay = GetTemplateDisplay(config.Template);

    static string ValueMarkup(string value, bool isDefault, bool isFocused)
    {
        if (isFocused) return $"[bold white]{Markup.Escape(value)}[/]";
        if (isDefault) return $"[grey dim][[ {Markup.Escape(value)} ]][/]";
        return $"[cyan]{Markup.Escape(value)}[/]";
    }

    string FieldRow(string id, string label, string value, bool isDefault)
    {
        var isFocused = focused == id;
        var indicator = isFocused ? "[bold blue]▶[/]" : " ";
        var labelStr = $"[grey]{Markup.Escape(label.PadRight(20))}[/]";
        return $" {indicator} {labelStr}  {ValueMarkup(value, isDefault, isFocused)}";
    }

    var rows = new List<IRenderable>();
    rows.Add(new Markup("[bold]Create Sandbox[/]"));
    rows.Add(new Rule { Style = Style.Parse("grey") });
    rows.Add(new Markup(FieldRow("name", "Name", config.Name, config.Name == workspaceFolderName)));
    rows.Add(new Markup(FieldRow("agent", "Agent", agentDisplay, agentIsDefault)));
    rows.Add(new Markup(FieldRow("workdir", "Working Directory", config.WorkingDirectory, config.WorkingDirectory == ".")));
    rows.Add(new Markup(FieldRow("workspace_mode", "Workspace Mode", config.WorkspaceMode.Name, !config.WorkspaceMode.UseClone)));
    rows.Add(new Markup(FieldRow("template", "Template", templateDisplay, config.Template is null)));

    // Kits section
    rows.Add(new Markup(""));
    rows.Add(new Markup("[grey]  Kits[/]"));
    rows.Add(new Rule { Style = Style.Parse("grey dim") });

    if (config.Kits.Count == 0)
        rows.Add(new Markup("[grey dim]    (no kits added)[/]"));

    for (var i = 0; i < config.Kits.Count; i++)
    {
        var kit = config.Kits[i];
        var kitId = $"kit:{i}";
        var isFocused = focused == kitId;
        var indicator = isFocused ? "[bold blue]▶[/]" : " ";
        var kitMarkup = isFocused
            ? $"[bold white]× {Markup.Escape(kit.DisplayName)}[/]"
            : $"[cyan]× {Markup.Escape(kit.DisplayName)}[/]";
        rows.Add(new Markup($" {indicator} {kitMarkup}"));
    }

    {
        var isFocused = focused == "add_kit";
        var indicator = isFocused ? "[bold blue]▶[/]" : " ";
        var addKitText = isFocused ? "[bold white on blue] + Add kit [/]" : "[grey]+ Add kit[/]";
        rows.Add(new Markup($" {indicator} {addKitText}"));
    }

    // Command preview
    var agentId = config.CustomAgentId ?? config.SelectedAgent.Id;
    var kitUrls = config.Kits.Select(k => k.Url).ToList();
    var displayTemplateName = config.Template?.Source is TemplateSource.GitRepo or TemplateSource.Local
        ? "<image-id>"
        : config.Template?.ImageName;
    var command = BuildDisplayCommand(config.Name, displayTemplateName, kitUrls, config.WorkspaceMode, agentId, config.WorkingDirectory);

    rows.Add(new Markup(""));
    rows.Add(new Panel(new Markup($"[dim]{Markup.Escape(command)}[/]"))
    {
        Header = new PanelHeader("[grey]Command[/]"),
        Border = BoxBorder.Rounded,
        BorderStyle = Style.Parse("grey"),
        Padding = new Padding(1, 0),
    });

    // Action buttons (stacked)
    rows.Add(new Markup(""));
    {
        var isFocused = focused == "create";
        var indicator = isFocused ? "[bold blue]▶[/]" : " ";
        var text = isFocused ? "[bold white on blue] Create Sandbox [/]" : "[grey]Create Sandbox[/]";
        rows.Add(new Markup($" {indicator} {text}"));
    }
    {
        var isFocused = focused == "cancel";
        var indicator = isFocused ? "[bold blue]▶[/]" : " ";
        var text = isFocused ? "[bold white on blue] Cancel [/]" : "[grey]Cancel[/]";
        rows.Add(new Markup($" {indicator} {text}"));
    }

    // Contextual hints footer
    rows.Add(new Rule { Style = Style.Parse("grey") });
    rows.Add(new Markup($"[grey dim]{GetContextualHints(focused)}[/]"));

    return new Rows(rows);
}

static string GetTemplateDisplay(TemplateConfig? template)
{
    if (template is null) return "None";
    return template.Source switch
    {
        TemplateSource.Registry => template.ImageName ?? "Unknown",
        TemplateSource.GitRepo =>
            string.Join("/", (template.DockerContext ?? "").Split([Path.DirectorySeparatorChar, '/'], StringSplitOptions.RemoveEmptyEntries).TakeLast(2)) +
            " > " + (template.DockerContext is not null && template.DockerfilePath is not null
                ? Path.GetRelativePath(template.DockerContext, template.DockerfilePath)
                : template.DockerfilePath ?? "Unknown") +
            (string.IsNullOrEmpty(template.Branch) ? "" : $" ({template.Branch})"),
        TemplateSource.Local => template.DockerfilePath ?? "Unknown",
        _ => "Unknown"
    };
}

static string GetContextualHints(string focused) => focused switch
{
    "name" => "↑/↓ navigate   Enter  enter text   w  workspace folder   d  default",
    "agent" => "↑/↓ navigate   Enter  select",
    "workdir" => "↑/↓ navigate   Enter  enter text   d  default (.)",
    "workspace_mode" => "↑/↓ navigate   Enter  select",
    "template" => "↑/↓ navigate   Enter  edit   d  default (none)",
    "add_kit" => "↑/↓ navigate   Enter  add kit",
    "create" => "↑/↓ navigate   Enter  create sandbox",
    "cancel" => "↑/↓ navigate   Enter  cancel",
    var s when s.StartsWith("kit:") => "↑/↓ navigate   Enter  confirm remove   r  remove",
    _ => "↑/↓ navigate   Enter  select"
};

static void HandleKeyPress(ConsoleKeyInfo key, TuiState state, string workspaceFolderName)
{
    var items = GetFocusableItems(state);
    var focused = items[Math.Clamp(state.FocusIndex, 0, items.Count - 1)];

    switch (key.Key)
    {
        case ConsoleKey.UpArrow:
            state.FocusIndex = Math.Max(0, state.FocusIndex - 1);
            break;
        case ConsoleKey.DownArrow:
            state.FocusIndex = Math.Min(items.Count - 1, state.FocusIndex + 1);
            break;
        case ConsoleKey.Tab:
            state.FocusIndex = (state.FocusIndex + 1) % items.Count;
            break;
        case ConsoleKey.Enter:
            state.PendingAction = focused switch
            {
                "name" => "edit_name",
                "agent" => "edit_agent",
                "workdir" => "edit_workdir",
                "workspace_mode" => "edit_workspace_mode",
                "template" => "edit_template",
                "add_kit" => "add_kit",
                "create" => "create",
                "cancel" => "cancel",
                var s when s.StartsWith("kit:") => s,
                _ => null
            };
            break;
        case ConsoleKey.Delete:
        case ConsoleKey.Backspace:
            if (focused.StartsWith("kit:"))
                state.PendingAction = $"remove_{focused}";
            break;
        case ConsoleKey.Escape:
            state.PendingAction = "cancel";
            break;
        default:
            HandleShortcutKey(key.KeyChar, focused, state, workspaceFolderName);
            break;
    }
}

static void HandleShortcutKey(char ch, string focused, TuiState state, string workspaceFolderName)
{
    switch (ch)
    {
        case 'w' when focused == "name":
            state.Config.Name = workspaceFolderName;
            break;
        case 'd' when focused == "name":
            state.Config.Name = workspaceFolderName;
            break;
        case 'd' when focused == "workdir":
            state.Config.WorkingDirectory = ".";
            break;
        case 'd' when focused == "template":
            state.Config.Template = null;
            break;
        case 'r' when focused.StartsWith("kit:"):
            state.PendingAction = $"remove_{focused}";
            break;
    }
}

static void AddRecentUrl(List<string> urls, string url)
{
    urls.Remove(url);
    urls.Insert(0, url);
    while (urls.Count > 10) urls.RemoveAt(urls.Count - 1);
    SaveRecentUrls(urls);
}

static void PrintSbxCommand(string name, string? templateName, List<string> kitUrls, WorkspaceMode workspaceMode, string agentId, string workDir)
{
    PrintCommand(BuildDisplayCommand(name, templateName, kitUrls, workspaceMode, agentId, workDir));
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

static string BuildDisplayCommand(string name, string? templateName, List<string> kitUrls, WorkspaceMode workspaceMode, string agentId, string workDir)
{
    var parts = new List<string> { "sbx create", $"--name \"{name}\"" };
    if (templateName is not null) parts.Add($"--template \"{templateName}\"");
    if (kitUrls.Count > 0) parts.Add(string.Join(" ", kitUrls.Select(u => $"--kit \"{u}\"")));
    if (workspaceMode.UseClone) parts.Add("--clone");
    parts.Add(agentId);
    parts.Add($"\"{workDir}\"");
    return string.Join(" ", parts);
}

static void PrintCommand(string command)
{
    AnsiConsole.MarkupLine("[bold]Command:[/]");
    AnsiConsole.MarkupLine($"[blue]{Markup.Escape(command)}[/]");
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

    if (template.Source == TemplateSource.GitRepo)
    {
        if (string.IsNullOrEmpty(template.Branch))
            await RunProcess("git", ["checkout", "--detach", "origin/HEAD"], template.DockerContext);
        else
            await RunProcess("git", ["checkout", "--detach", $"origin/{template.Branch}"], template.DockerContext);
    }

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

class SandboxConfig
{
    public required string Name { get; set; }
    public required AgentOption SelectedAgent { get; set; }
    public string? CustomAgentId { get; set; }
    public required string WorkingDirectory { get; set; }
    public required WorkspaceMode WorkspaceMode { get; set; }
    public TemplateConfig? Template { get; set; }
    public List<KitEntry> Kits { get; set; } = [];
}

class TuiState
{
    public required SandboxConfig Config { get; set; }
    public int FocusIndex { get; set; }
    public required List<string> RecentUrls { get; set; }
    public required HashSet<string> FetchedRepos { get; set; }
    public string? PendingAction { get; set; }
}

record KitEntry(string Url, string DisplayName);
record AgentOption(string Id, string DisplayName, string? Description);
record Kit(string? Directory, string DisplayName, string? Description);
record WorkspaceMode(string Name, string Description, bool UseClone);
record TemplateSourceOption(TemplateSource Source, string DisplayName);
record TemplateConfig(TemplateSource Source, string ImageName, string? DockerfilePath, string? DockerContext, string? Branch = null);
enum TemplateSource { Registry, GitRepo, Local }
