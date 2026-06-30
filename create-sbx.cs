#!/usr/bin/env -S dotnet run
#:package Spectre.Console@0.57.0
#:package System.CommandLine@2.0.9

using System.CommandLine;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;

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

    var workspaceModes = new List<WorkspaceMode>
    {
        new("Direct", "Mount the host directory directly into the sandbox", false),
        new("Clone", "Clone the repository into the sandbox", true),
    };

    var config = new SandboxConfig
    {
        Name = workspaceFolderName,
        SelectedAgent = builtInAgents[0],
        WorkingDirectory = ".",
        WorkspaceMode = workspaceModes[0],
        Template = null,
        Kits = [],
    };

    var tuiState = new TuiState
    {
        Config = config,
        BuiltInAgents = builtInAgents,
        WorkspaceModes = workspaceModes,
        FocusIndex = 0,
        RecentUrls = LoadRecentUrls(),
        FetchedRepos = [],
    };

    var tuiResult = await RunTuiAsync(tuiState, workspaceFolderName);
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

async Task<int> RunTuiAsync(TuiState state, string workspaceFolderName)
{
    async Task DispatchActionAsync(string action)
    {
        switch (action)
        {
            default:
                {
                    if (action.StartsWith("fetch_template_git:"))
                    {
                        var body = action["fetch_template_git:".Length..];
                        var sep = body.IndexOf('\n');
                        var repoUrl = sep >= 0 ? body[..sep] : body;
                        var tBranch = sep >= 0 ? body[(sep + 1)..] : "";
                        var (tOwner, tRepo) = ParseGitHubUrl(repoUrl);
                        if (tOwner is null || tRepo is null)
                        {
                            state.InlineEdit = new InlineEditState
                            {
                                FieldId = "template_git_error",
                                LoadingMessage = "Invalid GitHub repository URL. Expected: https://github.com/owner/repo",
                            };
                            break;
                        }

                        AddRecentUrl(state.RecentUrls, repoUrl);

                        state.InlineEdit = new InlineEditState
                        {
                            FieldId = "template_git_loading",
                            LoadingMessage = "Fetching repository...",
                        };
                        AnsiConsole.Clear();
                        AnsiConsole.Write(RenderForm(state, workspaceFolderName));

                        string tCloneDir;
                        List<string> dockerfiles;
                        try
                        {
                            tCloneDir = await EnsureRepo(tOwner, tRepo, tBranch, null, state.FetchedRepos);
                            dockerfiles = FindDockerfiles(tCloneDir);
                        }
                        catch (Exception ex)
                        {
                            state.InlineEdit = new InlineEditState
                            {
                                FieldId = "template_git_error",
                                LoadingMessage = $"Failed to fetch repository: {ex.Message}",
                            };
                            break;
                        }

                        if (dockerfiles.Count == 0)
                        {
                            state.InlineEdit = new InlineEditState
                            {
                                FieldId = "template_git_error",
                                LoadingMessage = "No Dockerfiles found in the repository.",
                            };
                            break;
                        }

                        state.InlineEdit = new InlineEditState
                        {
                            FieldId = "template_git_dockerfile",
                            OptionLabels = [.. dockerfiles],
                            CurrentIndex = 0,
                            OriginalIndex = 0,
                            ContextValue = $"{tCloneDir}\n{tBranch}",
                        };
                    }
                    else if (action.StartsWith("fetch_kit:"))
                    {
                        var body = action["fetch_kit:".Length..];
                        var sep = body.IndexOf('\n');
                        var repoUrl = sep >= 0 ? body[..sep] : body;
                        var branch = sep >= 0 ? body[(sep + 1)..] : "";
                        var (owner, repo) = ParseGitHubUrl(repoUrl);
                        if (owner is null || repo is null)
                        {
                            state.InlineEdit = new InlineEditState
                            {
                                FieldId = "add_kit_error",
                                LoadingMessage = "Invalid GitHub repository URL. Expected: https://github.com/owner/repo",
                            };
                            break;
                        }

                        AddRecentUrl(state.RecentUrls, repoUrl);

                        state.InlineEdit = new InlineEditState
                        {
                            FieldId = "add_kit_loading",
                            LoadingMessage = "Fetching kits...",
                        };
                        AnsiConsole.Clear();
                        AnsiConsole.Write(RenderForm(state, workspaceFolderName));
                        state.InlineEdit = null;

                        List<Kit> kits;
                        try
                        {
                            var cloneDir = await EnsureRepo(owner, repo, branch, null, state.FetchedRepos);
                            kits = FindKits(cloneDir);
                        }
                        catch (Exception ex)
                        {
                            state.InlineEdit = new InlineEditState
                            {
                                FieldId = "add_kit_error",
                                LoadingMessage = $"Failed to fetch repository: {ex.Message}",
                            };
                            break;
                        }

                        if (kits.Count == 0)
                        {
                            state.InlineEdit = new InlineEditState
                            {
                                FieldId = "add_kit_error",
                                LoadingMessage = "No kits found in the repository.",
                            };
                            break;
                        }

                        state.PendingKits = kits;
                        state.PendingKitOwner = owner;
                        state.PendingKitRepo = repo;
                        state.PendingKitBranch = branch;

                        var kitLabels = kits.Select(k => k.Description is not null
                            ? $"{k.DisplayName} - {k.Description}"
                            : k.DisplayName).ToList();
                        state.InlineEdit = new InlineEditState
                        {
                            FieldId = "add_kit_multiselect",
                            OptionLabels = kitLabels,
                            SelectedIndices = new HashSet<int>(),
                            CurrentIndex = 0,
                            OriginalIndex = 0,
                            ContextValue = repoUrl,
                        };
                    }
                    else if (action.StartsWith("kit:"))
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
    if (state.Config.Kits.Count == 0)
        items.Add("kits");
    else
        for (var i = 0; i < state.Config.Kits.Count; i++)
            items.Add($"kit:{i}");
    items.Add("create");
    items.Add("cancel");
    return items;
}

static IRenderable RenderForm(TuiState state, string workspaceFolderName)
{
    var config = state.Config;
    var items = GetFocusableItems(state);
    var focusedId = items[Math.Clamp(state.FocusIndex, 0, items.Count - 1)];
    var edit = state.InlineEdit;

    var isFieldsEdit = edit?.FieldId is "name" or "agent" or "workdir" or "workspace_mode" or "custom_agent_id"
        or "template_source" or "template_registry" or "template_git_url_select" or "template_git_url" or "template_git_branch"
        or "template_git_loading" or "template_git_error" or "template_git_dockerfile" or "template_local_path";
    var isKitsEdit = edit?.FieldId is "add_kit_url" or "add_kit_url_select" or "add_kit_branch"
        or "add_kit_loading" or "add_kit_error" or "add_kit_multiselect";

    var isFieldsFocused = focusedId is "name" or "agent" or "workdir" or "workspace_mode" or "template"
        || isFieldsEdit;
    var isKitsFocused = focusedId == "kits" || focusedId.StartsWith("kit:") || isKitsEdit;

    var agentDisplay = config.CustomAgentId is not null
        ? $"Custom: {config.CustomAgentId}"
        : $"{config.SelectedAgent.DisplayName} ({config.SelectedAgent.Id})";

    // Live value shown in the left panel; selection edits preview current option, text edits show original
    string LiveValue(string id, string value)
    {
        if (id == "agent" && edit?.FieldId == "custom_agent_id" && edit.TextBuffer != null)
            return $"Custom: {edit.TextBuffer}";
        if (id == "template" && edit?.FieldId is "template_registry" or "template_git_url" or "template_local_path" && edit.TextBuffer != null)
            return edit.TextBuffer;
        if (id == "template" && edit?.FieldId == "template_git_branch")
            return edit.ContextValue ?? value;
        return value;
    }

    string FieldRow(string id, string label, string rawValue)
    {
        var displayValue = LiveValue(id, rawValue);
        var isFocused = focusedId == id || edit?.FieldId == id
            || (id == "agent" && edit?.FieldId == "custom_agent_id")
            || (id == "template" && edit?.FieldId is "template_source" or "template_registry" or "template_git_url_select" or "template_git_url" or "template_git_branch" or "template_git_loading" or "template_git_error" or "template_git_dockerfile" or "template_local_path");
        if (isFocused)
            return $"[green]▶ {Markup.Escape(label.PadRight(20))}  {Markup.Escape(displayValue)}[/]";
        return $"  {Markup.Escape(label.PadRight(20))}  [white]{Markup.Escape(rawValue)}[/]";
    }

    // Spacer — a space character guarantees a non-empty line in Rows
    var spacer = new Markup(" ");

    // Fields panel (Details)
    var fieldRows = new List<IRenderable>
    {
        new Markup(FieldRow("name", "Name", config.Name)),
        new Markup(FieldRow("agent", "Agent", agentDisplay)),
        new Markup(FieldRow("workdir", "Working Directory", config.WorkingDirectory)),
        new Markup(FieldRow("workspace_mode", "Workspace Mode", config.WorkspaceMode.Name)),
        new Markup(FieldRow("template", "Template", GetTemplateDisplay(config.Template))),
    };

    var fieldsBorderColor = isFieldsFocused ? Color.Green : Color.Grey;
    var fieldsPanelHeader = isFieldsFocused ? "[bold green]Details[/]" : "[bold]Details[/]";
    var fieldsPanel = new Panel(new Rows(fieldRows))
        .Header(fieldsPanelHeader)
        .Border(BoxBorder.Rounded)
        .BorderColor(fieldsBorderColor)
        .Padding(1, 1, 1, 1)
        .Expand();

    // Kits panel
    var kitRows = new List<IRenderable> { spacer };
    if (config.Kits.Count == 0)
    {
        kitRows.Add(new Markup("  [grey]No kits added[/]"));
    }
    else
    {
        for (var i = 0; i < config.Kits.Count; i++)
        {
            var kit = config.Kits[i];
            var isFocused = focusedId == $"kit:{i}";
            kitRows.Add(new Markup(isFocused
                ? $"[green]▶ {Markup.Escape(kit.DisplayName)}[/]"
                : $"  [white]{Markup.Escape(kit.DisplayName)}[/]"));
            kitRows.Add(spacer);
        }
    }
    kitRows.Add(spacer);

    var kitsBorderColor = isKitsFocused ? Color.Green : Color.Grey;
    var kitsPanelHeader = isKitsFocused ? "[green]Kits[/]" : "[grey]Kits[/]";
    var kitsPanel = new Panel(new Rows(kitRows))
        .Header(kitsPanelHeader)
        .Border(BoxBorder.Rounded)
        .BorderColor(kitsBorderColor)
        .Padding(1, 0, 1, 0)
        .Expand();

    // Preview panel
    var agentId = config.CustomAgentId ?? config.SelectedAgent.Id;
    var kitUrls = config.Kits.Select(k => k.Url).ToList();
    var displayTemplateName = config.Template?.Source is TemplateSource.GitRepo or TemplateSource.Local
        ? "<image-id>"
        : config.Template?.ImageName;
    var command = BuildDisplayCommand(config.Name, displayTemplateName, kitUrls, config.WorkspaceMode, agentId, config.WorkingDirectory);

    var previewRows = new List<IRenderable> { new Markup($"[cyan]{Markup.Escape(command)}[/]") };
    if (config.Template?.Source is TemplateSource.GitRepo or TemplateSource.Local)
    {
        previewRows.Add(new Markup(" "));
        previewRows.Add(new Markup($"[yellow]Dockerfile {Markup.Escape(config.Template.DockerfilePath ?? "")} will be built before creating the sandbox.[/]"));
    }

    var commandPanel = new Panel(new Rows(previewRows))
        .Header("[grey]Preview[/]")
        .Border(BoxBorder.Rounded)
        .BorderColor(Color.Grey)
        .Padding(1, 0, 1, 0)
        .Expand();

    // Action buttons — 2-space indent when not focused so ▶ prefix doesn't cause text shift
    var createMarkup = focusedId == "create" ? "[green]▶ Create Sandbox[/]" : "  [white]Create Sandbox[/]";
    var cancelMarkup = focusedId == "cancel" ? "[green]▶ Exit[/]" : "  [white]Exit[/]";

    var previewPanelHeight = previewRows.Count + 2;
    var bottomHeight = 8 + previewPanelHeight;

    var leftContent = new Rows(new IRenderable[] { fieldsPanel, spacer, kitsPanel });
    var bottomContent = new Rows(new IRenderable[]
    {
        spacer,
        new Markup(createMarkup),
        new Markup(cancelMarkup),
        spacer,
        spacer,
        commandPanel,
        spacer,
        new Markup("  " + GetContextualHints(focusedId, edit)),
    });

    var layout = new Layout("root")
        .SplitRows(
            new Layout("title").Size(1),
            new Layout("content").SplitColumns(
                new Layout("left"),
                new Layout("right")
            ),
            new Layout("bottom").Size(bottomHeight)
        );

    layout["title"].Update(new Markup("[bold]Create Sandbox[/]"));
    layout["left"].Update(leftContent);
    layout["bottom"].Update(bottomContent);

    if (edit != null)
    {
        layout["right"].Update(BuildEditPanel(edit));
    }
    else
    {
        layout["right"].Invisible();
    }

    return layout;
}

static Panel BuildEditPanel(InlineEditState edit, int targetContentLines = 0)
{
    const int MinWidth = 46;

    string title;
    IRenderable mainContent;

    if (edit.LoadingMessage != null)
    {
        title = edit.FieldId switch
        {
            "template_git_loading" => "Fetching Repository",
            "template_git_error"   => "Error",
            "add_kit_loading"      => "Fetching Kits",
            "add_kit_error"        => "Error",
            _                      => "Loading"
        };
        mainContent = new Markup($"[grey]{Markup.Escape(edit.LoadingMessage)}[/]");
    }
    else if (edit.TextBuffer != null)
    {
        title = edit.FieldId switch
        {
            "name"                => "Edit Name",
            "workdir"             => "Edit Working Directory",
            "custom_agent_id"     => "Custom Agent ID",
            "template_registry"   => "Docker Image Name",
            "template_git_url"    => "Template Repository URL",
            "template_git_branch" => "Template Branch (leave blank for default)",
            "template_local_path" => "Dockerfile Path",
            "add_kit_url"         => "Kit Repository URL",
            "add_kit_branch"      => "Kit Branch (leave blank for default)",
            _                     => "Edit"
        };
        var buf = edit.TextBuffer;
        var padded = buf.PadRight(Math.Max(MinWidth, buf.Length + 1));
        mainContent = new Markup($"[white]{Markup.Escape(padded)}[/][green]▌[/]");
    }
    else
    {
        title = edit.FieldId switch
        {
            "agent"                   => "Select Agent",
            "workspace_mode"          => "Select Workspace Mode",
            "template_source"         => "Select Template Source",
            "template_git_url_select" => "Select Template Repository",
            "template_git_dockerfile" => "Select Dockerfile",
            "add_kit_url_select"      => "Select Repository",
            "add_kit_multiselect"     => "Select Kits",
            _                         => "Select"
        };
        mainContent = BuildDropdown(edit, MinWidth);
    }

    var sp = new Markup(" ");
    var hints = edit.LoadingMessage != null
        ? "[grey][white]Esc[/] dismiss[/]"
        : edit.SelectedIndices != null
            ? "[grey][white]Space[/] toggle   [white]Enter[/] confirm   [white]Esc[/] cancel[/]"
            : "[grey][white]Enter[/] confirm   [white]Esc[/] cancel[/]";
    var panelRows = new List<IRenderable>
    {
        sp,
        mainContent,
        sp,
        new Markup(hints),
        sp,
    };

    while (targetContentLines > 0 && panelRows.Count < targetContentLines)
        panelRows.Add(new Markup(" "));

    return new Panel(new Rows(panelRows))
        .Header($"[green]{Markup.Escape(title)}[/]")
        .Border(BoxBorder.Rounded)
        .BorderColor(Color.Green)
        .Padding(2, 0, 2, 0)
        .Expand();
}

static IRenderable BuildDropdown(InlineEditState edit, int minWidth = 40)
{
    var rows = new List<IRenderable>();
    var pageSize = 8;
    var start = Math.Max(0, edit.CurrentIndex - pageSize / 2);
    start = Math.Min(start, Math.Max(0, edit.OptionLabels!.Count - pageSize));
    var end = Math.Min(edit.OptionLabels.Count, start + pageSize);

    if (start > 0) rows.Add(new Markup("[grey]↑ more[/]"));
    for (var i = start; i < end; i++)
    {
        var raw = edit.OptionLabels[i];
        var padded = raw.PadRight(Math.Max(minWidth, raw.Length + 1));
        var label = Markup.Escape(padded);
        if (edit.SelectedIndices != null)
        {
            var check = edit.SelectedIndices.Contains(i) ? "[[x]]" : "[[ ]]";
            if (i == edit.CurrentIndex)
                rows.Add(new Markup($"[green]▶ {check} {label}[/]"));
            else if (edit.SelectedIndices.Contains(i))
                rows.Add(new Markup($"  {check} [white]{label}[/]"));
            else
                rows.Add(new Markup($"  {check} [grey]{label}[/]"));
        }
        else
        {
            rows.Add(i == edit.CurrentIndex
                ? new Markup($"[green]▶ {label}[/]")
                : new Markup($"  [grey]{label}[/]"));
        }
    }
    if (end < edit.OptionLabels.Count) rows.Add(new Markup("[grey]↓ more[/]"));

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

static string GetContextualHints(string focused, InlineEditState? edit)
{
    static string K(string key) => $"[white]{Markup.Escape(key)}[/]";
    static string D(string desc) => $"[grey]{Markup.Escape(desc)}[/]";

    if (edit != null)
    {
        if (edit.SelectedIndices != null)
            return $"{K("↑/↓")} {D("navigate")}   {K("Space")} {D("toggle")}   {K("Enter")} {D("confirm")}   {K("Esc")} {D("cancel")}";
        return $"{K("↑/↓")} {D("navigate")}   {K("Enter")} {D("confirm")}   {K("Esc")} {D("cancel")}";
    }

    return focused switch
    {
        "name" => $"{K("↑/↓")} {D("navigate")}   {K("Enter")} {D("edit")}   {K("c")} {D("current folder")}   {K("d")} {D("default")}",
        "agent" => $"{K("↑/↓")} {D("navigate")}   {K("Enter")} {D("select")}",
        "workdir" => $"{K("↑/↓")} {D("navigate")}   {K("Enter")} {D("edit")}   {K("d")} {D("default (.)")}",
        "workspace_mode" => $"{K("↑/↓")} {D("navigate")}   {K("Enter")} {D("select")}",
        "template" => $"{K("↑/↓")} {D("navigate")}   {K("Enter")} {D("edit")}   {K("d")} {D("default (none)")}",
        "kits" => $"{K("↑/↓")} {D("navigate")}   {K("a")} {D("add kit")}",
        "create" => $"{K("↑/↓")} {D("navigate")}   {K("Enter")} {D("create sandbox")}",
        "cancel" => $"{K("↑/↓")} {D("navigate")}   {K("Enter")} {D("exit")}",
        var s when s.StartsWith("kit:") => $"{K("↑/↓")} {D("navigate")}   {K("Enter")} {D("confirm remove")}   {K("r")} {D("remove")}   {K("a")} {D("add kit")}",
        _ => $"{K("↑/↓")} {D("navigate")}   {K("Enter")} {D("select")}"
    };
}

static void HandleKeyPress(ConsoleKeyInfo key, TuiState state, string workspaceFolderName)
{
    if (state.InlineEdit != null)
    {
        HandleInlineEditKey(key, state);
        return;
    }

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
            HandleEnterKey(focused, state);
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

static void HandleEnterKey(string focused, TuiState state)
{
    switch (focused)
    {
        case "name":
            state.InlineEdit = new InlineEditState
            {
                FieldId = "name",
                TextBuffer = state.Config.Name,
                TextOriginal = state.Config.Name,
            };
            break;
        case "agent":
            {
                var labels = state.BuiltInAgents
                    .Select(a => a.Description is not null
                        ? $"{a.DisplayName} ({a.Id}) - {a.Description}"
                        : $"{a.DisplayName} ({a.Id})")
                    .Append("Custom agent...")
                    .ToList();
                var current = state.BuiltInAgents.FindIndex(a => a.Id == state.Config.SelectedAgent.Id);
                if (current < 0) current = 0;
                state.InlineEdit = new InlineEditState
                {
                    FieldId = "agent",
                    OptionLabels = labels,
                    CurrentIndex = current,
                    OriginalIndex = current,
                };
                break;
            }
        case "workdir":
            state.InlineEdit = new InlineEditState
            {
                FieldId = "workdir",
                TextBuffer = state.Config.WorkingDirectory,
                TextOriginal = state.Config.WorkingDirectory,
            };
            break;
        case "workspace_mode":
            {
                var labels = state.WorkspaceModes.Select(m => $"{m.Name} - {m.Description}").ToList();
                var current = state.WorkspaceModes.FindIndex(m => m.Name == state.Config.WorkspaceMode.Name);
                if (current < 0) current = 0;
                state.InlineEdit = new InlineEditState
                {
                    FieldId = "workspace_mode",
                    OptionLabels = labels,
                    CurrentIndex = current,
                    OriginalIndex = current,
                };
                break;
            }
        case "template":
            {
                var options = new List<string>
            {
                "None",
                "Docker image (Registry)",
                "Dockerfile - Git repository",
                "Dockerfile - local path",
            };
                var currentIdx = state.Config.Template?.Source switch
                {
                    TemplateSource.Registry => 1,
                    TemplateSource.GitRepo => 2,
                    TemplateSource.Local => 3,
                    _ => 0,
                };
                state.InlineEdit = new InlineEditState
                {
                    FieldId = "template_source",
                    OptionLabels = options,
                    CurrentIndex = currentIdx,
                    OriginalIndex = currentIdx,
                };
                break;
            }
        case "kits":
            StartAddKitInlineEdit(state);
            break;
        case "create":
            state.PendingAction = "create";
            break;
        case "cancel":
            state.PendingAction = "cancel";
            break;
        default:
            if (focused.StartsWith("kit:"))
                state.PendingAction = focused;
            break;
    }
}

static void StartAddKitInlineEdit(TuiState state)
{
    if (state.RecentUrls.Count > 0)
    {
        state.InlineEdit = new InlineEditState
        {
            FieldId = "add_kit_url_select",
            OptionLabels = [.. state.RecentUrls, "Enter new URL..."],
            CurrentIndex = 0,
            OriginalIndex = 0,
        };
    }
    else
    {
        state.InlineEdit = new InlineEditState
        {
            FieldId = "add_kit_url",
            TextBuffer = "",
            TextOriginal = "",
        };
    }
}

static void HandleInlineEditKey(ConsoleKeyInfo key, TuiState state)
{
    var edit = state.InlineEdit!;

    if (edit.TextBuffer != null)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                switch (edit.FieldId)
                {
                    case "name":
                        state.Config.Name = edit.TextBuffer;
                        break;
                    case "workdir":
                        state.Config.WorkingDirectory = edit.TextBuffer;
                        break;
                    case "template_registry":
                        if (!string.IsNullOrWhiteSpace(edit.TextBuffer))
                            state.Config.Template = new TemplateConfig(TemplateSource.Registry, edit.TextBuffer.Trim(), null, null);
                        break;
                    case "template_git_url":
                        if (!string.IsNullOrWhiteSpace(edit.TextBuffer))
                        {
                            state.InlineEdit = new InlineEditState
                            {
                                FieldId = "template_git_branch",
                                TextBuffer = "",
                                TextOriginal = "",
                                ContextValue = edit.TextBuffer.Trim().TrimEnd('/'),
                                PreviousEdit = edit,
                            };
                            return;
                        }
                        break; // empty = cancel
                    case "template_git_branch":
                        state.InlineEdit = null;
                        state.PendingAction = $"fetch_template_git:{edit.ContextValue}\n{edit.TextBuffer}";
                        return;
                    case "add_kit_url":
                        if (!string.IsNullOrWhiteSpace(edit.TextBuffer))
                        {
                            state.InlineEdit = new InlineEditState
                            {
                                FieldId = "add_kit_branch",
                                TextBuffer = "",
                                TextOriginal = "",
                                ContextValue = edit.TextBuffer.Trim().TrimEnd('/'),
                                PreviousEdit = edit,
                            };
                            return;
                        }
                        break; // empty = cancel
                    case "add_kit_branch":
                        state.InlineEdit = null;
                        state.PendingAction = $"fetch_kit:{edit.ContextValue}\n{edit.TextBuffer}";
                        return;
                    case "custom_agent_id":
                        if (!string.IsNullOrWhiteSpace(edit.TextBuffer))
                        {
                            var agentId = edit.TextBuffer.Trim();
                            state.Config.SelectedAgent = new AgentOption(agentId, agentId, null);
                            state.Config.CustomAgentId = agentId;
                        }
                        state.InlineEdit = null;
                        return;
                    case "template_local_path":
                        if (!string.IsNullOrWhiteSpace(edit.TextBuffer))
                        {
                            var path = Path.GetFullPath(edit.TextBuffer.Trim());
                            if (File.Exists(path))
                            {
                                var context = Path.GetDirectoryName(path)!;
                                var imageName = $"create-sbx-{Guid.NewGuid().ToString("N")[..8]}";
                                state.Config.Template = new TemplateConfig(TemplateSource.Local, imageName, path, context);
                                state.InlineEdit = null;
                            }
                            // else: invalid path — keep edit open so user can fix it
                            return;
                        }
                        state.InlineEdit = null; // empty = cancel
                        return;
                }
                state.InlineEdit = null;
                break;
            case ConsoleKey.Escape:
                state.InlineEdit = edit.PreviousEdit;
                break;
            case ConsoleKey.Backspace:
                if (edit.TextBuffer.Length > 0)
                    edit.TextBuffer = edit.TextBuffer[..^1];
                break;
            default:
                if (!char.IsControl(key.KeyChar))
                    edit.TextBuffer += key.KeyChar;
                break;
        }
    }
    else if (edit.OptionLabels != null)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                edit.CurrentIndex = Math.Max(0, edit.CurrentIndex - 1);
                break;
            case ConsoleKey.DownArrow:
                edit.CurrentIndex = Math.Min(edit.OptionLabels.Count - 1, edit.CurrentIndex + 1);
                break;
            case ConsoleKey.Enter:
                CommitSelectionEdit(edit, state);
                break;
            case ConsoleKey.Escape:
                state.InlineEdit = edit.PreviousEdit;
                break;
            case ConsoleKey.Spacebar:
                if (edit.SelectedIndices != null)
                {
                    if (!edit.SelectedIndices.Remove(edit.CurrentIndex))
                        edit.SelectedIndices.Add(edit.CurrentIndex);
                }
                break;
        }
    }
    else if (edit.LoadingMessage != null)
    {
        if (key.Key == ConsoleKey.Escape)
            state.InlineEdit = null;
    }
}

static void CommitSelectionEdit(InlineEditState edit, TuiState state)
{
    switch (edit.FieldId)
    {
        case "agent":
            if (edit.CurrentIndex < state.BuiltInAgents.Count)
            {
                state.Config.SelectedAgent = state.BuiltInAgents[edit.CurrentIndex];
                state.Config.CustomAgentId = null;
                state.InlineEdit = null;
            }
            else
            {
                var currentId = state.Config.CustomAgentId ?? "";
                state.InlineEdit = new InlineEditState
                {
                    FieldId = "custom_agent_id",
                    TextBuffer = currentId,
                    TextOriginal = currentId,
                    PreviousEdit = edit,
                };
            }
            break;

        case "workspace_mode":
            if (edit.CurrentIndex < state.WorkspaceModes.Count)
                state.Config.WorkspaceMode = state.WorkspaceModes[edit.CurrentIndex];
            state.InlineEdit = null;
            break;

        case "template_source":
            switch (edit.CurrentIndex)
            {
                case 0: // None
                    state.Config.Template = null;
                    state.InlineEdit = null;
                    break;
                case 1: // Registry — transition to text edit for image name
                    {
                        var current = state.Config.Template?.Source == TemplateSource.Registry
                            ? state.Config.Template.ImageName ?? "" : "";
                        state.InlineEdit = new InlineEditState
                        {
                            FieldId = "template_registry",
                            TextBuffer = current,
                            TextOriginal = current,
                        };
                        break;
                    }
                case 2: // Git repo — stay in panel for URL entry
                    if (state.RecentUrls.Count > 0)
                    {
                        state.InlineEdit = new InlineEditState
                        {
                            FieldId = "template_git_url_select",
                            OptionLabels = [.. state.RecentUrls, "Enter new URL..."],
                            CurrentIndex = 0,
                            OriginalIndex = 0,
                            PreviousEdit = edit,
                        };
                    }
                    else
                    {
                        state.InlineEdit = new InlineEditState
                        {
                            FieldId = "template_git_url",
                            TextBuffer = "",
                            TextOriginal = "",
                            PreviousEdit = edit,
                        };
                    }
                    break;
                case 3: // Local — inline path input
                {
                    var current = state.Config.Template?.Source == TemplateSource.Local
                        ? state.Config.Template.DockerfilePath ?? "" : "";
                    state.InlineEdit = new InlineEditState
                    {
                        FieldId = "template_local_path",
                        TextBuffer = current,
                        TextOriginal = current,
                        PreviousEdit = edit,
                    };
                    break;
                }
            }
            break;

        case "template_git_url_select":
            if (edit.CurrentIndex >= edit.OptionLabels!.Count - 1)
            {
                state.InlineEdit = new InlineEditState
                {
                    FieldId = "template_git_url",
                    TextBuffer = "",
                    TextOriginal = "",
                    PreviousEdit = edit,
                };
            }
            else
            {
                var url = edit.OptionLabels[edit.CurrentIndex].Trim().TrimEnd('/');
                state.InlineEdit = new InlineEditState
                {
                    FieldId = "template_git_branch",
                    TextBuffer = "",
                    TextOriginal = "",
                    ContextValue = url,
                    PreviousEdit = edit,
                };
            }
            break;

        case "add_kit_url_select":
            // Last option is "Enter new URL..."
            if (edit.CurrentIndex >= edit.OptionLabels!.Count - 1)
            {
                state.InlineEdit = new InlineEditState
                {
                    FieldId = "add_kit_url",
                    TextBuffer = "",
                    TextOriginal = "",
                    PreviousEdit = edit,
                };
            }
            else
            {
                var url = edit.OptionLabels[edit.CurrentIndex].Trim().TrimEnd('/');
                state.InlineEdit = new InlineEditState
                {
                    FieldId = "add_kit_branch",
                    TextBuffer = "",
                    TextOriginal = "",
                    ContextValue = url,
                    PreviousEdit = edit,
                };
            }
            break;

        case "template_git_dockerfile":
        {
            var ctxParts = edit.ContextValue!.Split('\n', 2);
            var cloneDir = ctxParts[0];
            var branch = ctxParts.Length > 1 ? ctxParts[1] : "";
            var dockerfile = edit.OptionLabels![edit.CurrentIndex];
            var imageName = $"create-sbx-{Guid.NewGuid().ToString("N")[..8]}";
            state.Config.Template = new TemplateConfig(TemplateSource.GitRepo, imageName,
                Path.Combine(cloneDir, dockerfile), cloneDir, branch);
            state.InlineEdit = null;
            break;
        }

        case "add_kit_multiselect":
        {
            if (state.PendingKits != null && edit.SelectedIndices != null)
            {
                var gitUrl = $"git+https://github.com/{state.PendingKitOwner}/{state.PendingKitRepo}.git";
                var refFragment = string.IsNullOrEmpty(state.PendingKitBranch) ? "" : $"&ref={Uri.EscapeDataString(state.PendingKitBranch)}";
                foreach (var i in edit.SelectedIndices.OrderBy(x => x))
                {
                    if (i >= state.PendingKits.Count) continue;
                    var k = state.PendingKits[i];
                    var url = k.Directory is not null
                        ? $"{gitUrl}#dir={k.Directory}{refFragment}"
                        : string.IsNullOrEmpty(refFragment) ? gitUrl : $"{gitUrl}#{refFragment.TrimStart('&')}";
                    state.Config.Kits.Add(new KitEntry(url, k.DisplayName));
                }
            }
            state.PendingKits = null;
            state.PendingKitOwner = null;
            state.PendingKitRepo = null;
            state.PendingKitBranch = null;
            state.InlineEdit = null;
            break;
        }
    }
}

static void HandleShortcutKey(char ch, string focused, TuiState state, string workspaceFolderName)
{
    switch (ch)
    {
        case 'c' when focused == "name":
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
        case 'a':
            StartAddKitInlineEdit(state);
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

static async Task<string> EnsureRepo(string owner, string repo, string branch, StatusContext? ctx, HashSet<string>? fetchedRepos = null)
{
    var cloneDir = Path.Combine(Path.GetTempPath(), "create-sbx", owner, repo);
    var repoKey = $"{owner}/{repo}#{branch}";

    if (fetchedRepos?.Contains(repoKey) == true)
        return cloneDir;

    if (Directory.Exists(Path.Combine(cloneDir, ".git")))
    {
        ctx?.Status("Fetching latest changes...");
        if (string.IsNullOrEmpty(branch))
            await RunProcess("git", ["fetch", "origin"], cloneDir);
        else
            await RunProcess("git", ["fetch", "origin", $"+refs/heads/{branch}:refs/remotes/origin/{branch}"], cloneDir);
    }
    else
    {
        ctx?.Status("Cloning repository...");
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
    public required List<AgentOption> BuiltInAgents { get; set; }
    public required List<WorkspaceMode> WorkspaceModes { get; set; }
    public int FocusIndex { get; set; }
    public required List<string> RecentUrls { get; set; }
    public required HashSet<string> FetchedRepos { get; set; }
    public string? PendingAction { get; set; }
    public InlineEditState? InlineEdit { get; set; }
    public List<Kit>? PendingKits { get; set; }
    public string? PendingKitOwner { get; set; }
    public string? PendingKitRepo { get; set; }
    public string? PendingKitBranch { get; set; }
}

class InlineEditState
{
    public required string FieldId { get; set; }

    // Text editing (non-null = in text edit mode)
    public string? TextBuffer { get; set; }
    public string TextOriginal { get; set; } = "";

    // Selection editing (non-null = in selection edit mode)
    public List<string>? OptionLabels { get; set; }
    public int CurrentIndex { get; set; }
    public int OriginalIndex { get; set; }

    // Set when this edit was entered from another — Esc restores it instead of closing entirely
    public InlineEditState? PreviousEdit { get; set; }

    // Carries auxiliary context (e.g. the confirmed URL when entering a branch)
    public string? ContextValue { get; set; }

    // Loading/error message (shown in panel instead of interactive content)
    public string? LoadingMessage { get; set; }

    // Selected indices for multi-select mode (non-null = multi-select enabled)
    public HashSet<int>? SelectedIndices { get; set; }
}

record KitEntry(string Url, string DisplayName);
record AgentOption(string Id, string DisplayName, string? Description);
record Kit(string? Directory, string DisplayName, string? Description);
record WorkspaceMode(string Name, string Description, bool UseClone);
record TemplateSourceOption(TemplateSource Source, string DisplayName);
record TemplateConfig(TemplateSource Source, string ImageName, string? DockerfilePath, string? DockerContext, string? Branch = null);
enum TemplateSource { Registry, GitRepo, Local }
