#!/usr/bin/env -S dotnet run
#:package Terminal.Gui@2.4.16
#:package System.CommandLine@2.0.9

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.CommandLine;
using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using TuiCommand = Terminal.Gui.Input.Command;

var rootCommand = new RootCommand("An interactive CLI for creating Docker Sandboxes using `sbx`");
rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
    await RunAsync());
return await rootCommand.Parse(args).InvokeAsync();

async Task<int> RunAsync()
{
    var defaultName = new DirectoryInfo(Directory.GetCurrentDirectory()).Name;
    var state = new SandboxFormState
    {
        Name = defaultName,
        AgentId = Options.BuiltInAgents[0].Id,
        WorkDir = ".",
        WorkspaceMode = Options.WorkspaceModes[0],
        Template = null,
        Kits = [],
    };

    Application.AppModel = AppModel.Inline;
    IApplication app = Application.Create().Init();
    var mainView = new MainView(state);
    app.Run(mainView);
    app.Dispose();

    if (mainView.Result != MainAction.Create)
        return 0;

    return await CreateSandboxAsync(state);
}

async Task<int> CreateSandboxAsync(SandboxFormState state)
{
    var template = state.Template;
    string? effectiveTemplateName = template?.Source == TemplateSource.Registry ? template.ImageName : null;

    if (template?.Source is TemplateSource.GitRepo or TemplateSource.Local)
    {
        try
        {
            effectiveTemplateName = await RepoTools.BuildAndLoadDockerImage(template);
        }
        catch (Exception ex)
        {
            RepoTools.WriteError($"Failed to build Docker image: {ex.Message}");
            return 1;
        }
    }

    var sbxArgs = new List<string> { "create", "--name", state.Name };
    if (effectiveTemplateName is not null) { sbxArgs.Add("--template"); sbxArgs.Add(effectiveTemplateName); }
    foreach (var kit in state.Kits) { sbxArgs.Add("--kit"); sbxArgs.Add(kit.Url); }
    if (state.WorkspaceMode.UseClone) sbxArgs.Add("--clone");
    sbxArgs.Add(state.AgentId);
    sbxArgs.Add(state.WorkDir);

    Console.WriteLine();
    Console.WriteLine("Command:");
    Console.WriteLine(RepoTools.BuildDisplayCommand(state.Name, effectiveTemplateName, state.Kits, state.WorkspaceMode, state.AgentId, state.WorkDir));
    Console.WriteLine();
    Console.WriteLine($"Creating sandbox {state.Name}...");
    Console.WriteLine();

    var psi = new ProcessStartInfo("sbx") { UseShellExecute = false };
    foreach (var arg in sbxArgs)
        psi.ArgumentList.Add(arg);
    using var proc = Process.Start(psi)!;
    await proc.WaitForExitAsync();
    if (proc.ExitCode != 0)
    {
        RepoTools.WriteError($"sbx exited with code {proc.ExitCode}");
        return proc.ExitCode;
    }

    return 0;
}

// ---------------------------------------------------------------------------
// Business logic (repo/kit discovery, Docker image build, process execution)
// ---------------------------------------------------------------------------

static class RepoTools
{
    public static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static string BuildDisplayCommand(string name, string? templateName, List<SelectedKit> kits, WorkspaceMode workspaceMode, string agentId, string workDir)
    {
        var parts = new List<string> { "sbx create", $"--name \"{name}\"" };
        if (templateName is not null) parts.Add($"--template \"{templateName}\"");
        if (kits.Count > 0) parts.Add(string.Join(" ", kits.Select(k => $"--kit \"{k.Url}\"")));
        if (workspaceMode.UseClone) parts.Add("--clone");
        parts.Add(agentId);
        parts.Add($"\"{workDir}\"");
        return string.Join(" ", parts);
    }

    public static string GetRecentUrlsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "create-sbx", "recent-urls.txt");

    public static List<string> LoadRecentUrls()
    {
        var path = GetRecentUrlsPath();
        if (!File.Exists(path)) return [];
        return [.. File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l))];
    }

    public static void SaveRecentUrls(List<string> urls)
    {
        var path = GetRecentUrlsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, urls);
    }

    public static void AddRecentUrl(List<string> urls, string url)
    {
        urls.Remove(url);
        urls.Insert(0, url);
        while (urls.Count > 10) urls.RemoveAt(urls.Count - 1);
        SaveRecentUrls(urls);
    }

    public static (string? owner, string? repo) ParseGitHubUrl(string url)
    {
        var match = Regex.Match(url, @"github\.com[/:](?<owner>[^/]+)/(?<repo>[^/.]+)");
        if (!match.Success) return (null, null);
        return (match.Groups["owner"].Value, match.Groups["repo"].Value);
    }

    public static async Task<string> EnsureRepo(string owner, string repo, string branch, HashSet<string> fetchedRepos)
    {
    var cloneDir = Path.Combine(Path.GetTempPath(), "create-sbx", owner, repo);
    var repoKey = $"{owner}/{repo}#{branch}";

    if (fetchedRepos.Contains(repoKey))
        return cloneDir;

    if (Directory.Exists(Path.Combine(cloneDir, ".git")))
    {
        if (string.IsNullOrEmpty(branch))
            await RunProcess("git", ["fetch", "origin"], cloneDir);
        else
            await RunProcess("git", ["fetch", "origin", $"+refs/heads/{branch}:refs/remotes/origin/{branch}"], cloneDir);
    }
    else
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cloneDir)!);
        await RunProcess("git", ["clone", $"https://github.com/{owner}/{repo}.git", cloneDir]);
    }

    if (string.IsNullOrEmpty(branch))
        await RunProcess("git", ["checkout", "--detach", "origin/HEAD"], cloneDir);
    else
        await RunProcess("git", ["checkout", "--detach", $"origin/{branch}"], cloneDir);

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

public static List<string> FindDockerfiles(string repoDir)
{
    var results = new List<string>();
    FindDockerfilesRecursive(repoDir, repoDir, results);
    results.Sort();
    return results;
}

public static void FindDockerfilesRecursive(string baseDir, string currentDir, List<string> results)
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

public static bool IsDockerfileName(string fileName) =>
    fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
    fileName.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase) ||
    fileName.EndsWith(".dockerfile", StringComparison.OrdinalIgnoreCase);

public static async Task<string> BuildAndLoadDockerImage(TemplateConfig template)
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

    Console.WriteLine($"Building Dockerfile {template.DockerfilePath}...");
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

        Console.WriteLine("Saving Docker image...");
        await RunProcess("docker", ["save", stableImageName, "-o", tarPath]);

        Console.WriteLine("Docker image built successfully.");

        Console.WriteLine("Loading template into sbx...");
        await RunProcessInteractive("sbx", ["template", "load", tarPath]);
        Console.WriteLine("Template loaded into sbx.");
    }
    else
    {
        Console.WriteLine($"Docker image built. Using cached template {stableImageName}.");
    }

    return stableImageName;
}

public static async Task<string> GetDockerImageId(string imageName)
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

public static async Task RunProcess(string fileName, string[] args, string? workDir = null)
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

public static async Task RunProcessInteractive(string fileName, string[] args, string? workDir = null)
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

public static string? ParseDisplayName(string yaml)
{
    var displayMatch = Regex.Match(yaml, @"^displayName:\s*(.+)$", RegexOptions.Multiline);
    if (displayMatch.Success)
        return displayMatch.Groups[1].Value.Trim().Trim('"');

    var nameMatch = Regex.Match(yaml, @"^name:\s*(.+)$", RegexOptions.Multiline);
    if (nameMatch.Success)
        return nameMatch.Groups[1].Value.Trim().Trim('"');

    return null;
}

public static string? ParseDescription(string yaml)
    {
        var match = Regex.Match(yaml, @"^description:\s*(.+)$", RegexOptions.Multiline);
        if (match.Success)
            return match.Groups[1].Value.Trim().Trim('"');
        return null;
    }
}

// ---------------------------------------------------------------------------
// Data model
// ---------------------------------------------------------------------------

record AgentOption(string Id, string DisplayName, string? Description)
{
    public string Label => Description is not null ? $"{DisplayName} ({Id}) - {Description}" : $"{DisplayName} ({Id})";
}

record WorkspaceMode(string Name, string Description, bool UseClone)
{
    public string Label => $"{Name} - {Description}";
}

record TemplateSourceOption(TemplateSource Source, string DisplayName);

record TemplateConfig(TemplateSource Source, string ImageName, string? DockerfilePath, string? DockerContext, string? Branch = null);

enum TemplateSource { Registry, GitRepo, Local }

record Kit(string? Directory, string DisplayName, string? Description)
{
    public string Label => Description is not null ? $"{DisplayName} - {Description}" : DisplayName;
}

record SelectedKit(string Url, string DisplayLabel);

enum MainAction { Exit, Create }

sealed class SandboxFormState
{
    public required string Name { get; set; }
    public required string AgentId { get; set; }
    public required string WorkDir { get; set; }
    public required WorkspaceMode WorkspaceMode { get; set; }
    public TemplateConfig? Template { get; set; }
    public required List<SelectedKit> Kits { get; set; }
}

static class Options
{
    public static readonly List<AgentOption> BuiltInAgents =
    [
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
    ];

    public static readonly List<WorkspaceMode> WorkspaceModes =
    [
        new("Direct", "Mount the host directory directly into the sandbox", false),
        new("Clone", "Clone the repository into the sandbox", true),
    ];

    public static readonly List<TemplateSourceOption> TemplateSources =
    [
        new(TemplateSource.Registry, "Docker image"),
        new(TemplateSource.GitRepo, "Dockerfile - Git repository"),
        new(TemplateSource.Local, "Dockerfile - local"),
    ];
}

// ---------------------------------------------------------------------------
// Popup building blocks
// ---------------------------------------------------------------------------

/// <summary>
/// Base for every field-editing popup: Cancel/Esc always leaves <see cref="Applied"/> false and
/// <see cref="Dialog{TResult}.Result"/> untouched, so callers never mistake "canceled" for "applied a value".
/// </summary>
abstract class FieldDialog<TResult> : Dialog<TResult>
{
    public bool Applied { get; private set; }
    public bool IsClosed { get; private set; }

    // A Dialog's Buttons unconditionally close it as soon as they're activated (click, or Enter while
    // focused) — there's no supported way for a subscriber to cancel that, which breaks flows that
    // need to sometimes stay open (validation failures, multi-step "Next"). So OkButton is disabled —
    // it's purely a label for the current action — and F2 is the real "confirm this step" key,
    // dispatched straight to the dialog itself, never touching a Button. Esc/Cancel work the same way,
    // for the same reason.
    protected Button OkButton { get; } = new() { Text = "OK", Enabled = false };
    protected Button CancelButton { get; } = new() { Text = "Cancel" };
    private bool _confirmEnabled = true;

    protected FieldDialog(string title)
    {
        Title = title;
        Buttons = [CancelButton, OkButton];

        AddCommand(TuiCommand.Accept, () => { if (_confirmEnabled && TryApply()) Close(applied: true); return true; });
        KeyBindings.Add(Key.F2, TuiCommand.Accept);

        AddCommand(TuiCommand.Cancel, () => { Close(applied: false); return true; });
        KeyBindings.Add(Key.Esc, TuiCommand.Cancel);
    }

    /// <summary>Set <see cref="Dialog{TResult}.Result"/> and return true to close; return false to keep the dialog open.</summary>
    protected abstract bool TryApply();

    protected void SetOkText(string text) => OkButton.Text = $"{text} (F2)";
    protected void SetOkEnabled(bool enabled) => _confirmEnabled = enabled;

    /// <summary>
    /// Lets plain Enter on <paramref name="view"/> also confirm the current step — most controls
    /// (<see cref="OptionSelector"/> especially) only update their own value on Enter, so this is the
    /// only way to both select an option and advance in one natural keystroke.
    /// </summary>
    protected void ConfirmOnAccept(View view) => view.Accepted += (_, _) => InvokeCommand(TuiCommand.Accept);

    /// <summary>
    /// Re-applies whatever focus a subclass wants once the dialog actually starts running.
    /// <see cref="View.SetFocus"/> calls made before a view has been through a layout pass (e.g. from a
    /// constructor, or right after swapping in a freshly-added step view) don't stick, so subclasses use
    /// <see cref="FocusDeferred"/> from here — and from wherever else they change focus — instead of
    /// calling <c>SetFocus()</c> directly.
    /// </summary>
    protected virtual void OnShown() { }

    /// <summary>Focuses <paramref name="view"/> after the next layout pass, once it actually has a Frame.</summary>
    protected void FocusDeferred(View? view)
    {
        if (view is null) return;
        App?.AddTimeout(TimeSpan.Zero, () =>
        {
            App?.LayoutAndDraw(true);
            view.SetFocus();
            return false;
        });
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning) OnShown();
    }

    private void Close(bool applied)
    {
        if (IsClosed) return;
        IsClosed = true;
        Applied = applied;
        App!.RequestStop();
    }
}

/// <summary>
/// A <see cref="FieldDialog{TResult}"/> whose content area is swapped between steps of a branching
/// flow (e.g. pick a template source, then fill in the details for that source) while staying a
/// single popup — Escape at any step aborts the whole flow.
/// </summary>
abstract class StepFieldDialog<TResult> : FieldDialog<TResult>
{
    private readonly View _content = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Auto(), CanFocus = true };
    private readonly Label _errorLabel = new() { X = 0, Width = Dim.Fill(), Height = 1, Visible = false };
    private Func<bool> _apply = () => false;
    private View? _currentView;

    protected StepFieldDialog(string title) : base(title)
    {
        Width = 76;
        Height = Dim.Auto();
        _errorLabel.Y = Pos.Bottom(_content) + 1;
        Add(_content, _errorLabel);
    }

    /// <summary>Replace the dialog's content with <paramref name="view"/> for the next step.</summary>
    protected void SetStep(View view, string okText, Func<bool> apply, View? confirmTarget = null, bool confirmOnEnter = true)
    {
        _content.RemoveAll();
        _content.Add(view);
        SetOkText(okText);
        _apply = apply;
        _errorLabel.Visible = false;
        _currentView = view;
        if (confirmOnEnter) ConfirmOnAccept(confirmTarget ?? view);
        FocusDeferred(view);
    }

    protected void ShowError(string message)
    {
        _errorLabel.Text = message;
        _errorLabel.Visible = true;
    }

    protected override bool TryApply() => _apply();

    protected override void OnShown() => FocusDeferred(_currentView);

    /// <summary>Groups a label with its input control for a step (e.g. a prompt label above a field).</summary>
    protected static View Row(params View[] children)
    {
        var row = new View { Width = Dim.Fill(), Height = Dim.Auto(), CanFocus = true };
        row.Add(children);
        return row;
    }
}

/// <summary>
/// Content view for "pick a repository URL": offers recently-used URLs plus a free-text entry,
/// shared by the Template (Git repository) and Kits (add kit) flows.
/// </summary>
sealed class RepoUrlPicker : View
{
    private readonly List<string> _recentUrls;
    private readonly OptionSelector? _recentSelector;
    private readonly TextField _urlField;

    /// <summary>
    /// Fires when Enter is pressed on whichever control is currently active. Controls don't bubble
    /// Enter/Accept to their superview by default, so this forwards it explicitly instead of relying
    /// on bubbling — see <see cref="FieldDialog{TResult}.ConfirmOnAccept"/> for the same pattern.
    /// </summary>
    public event EventHandler? Confirmed;

    public RepoUrlPicker(List<string> recentUrls)
    {
        _recentUrls = recentUrls;
        Width = Dim.Fill();
        Height = Dim.Auto();
        CanFocus = true;

        if (recentUrls.Count > 0)
        {
            var label = new Label { Text = "Repository URL:" };
            _recentSelector = new OptionSelector
            {
                Y = Pos.Bottom(label),
                Width = Dim.Fill(),
                Labels = [.. recentUrls, "Enter URL..."],
                Value = 0,
            };
            _urlField = new TextField
            {
                Y = Pos.Bottom(_recentSelector) + 1,
                Width = Dim.Fill(),
                Visible = false,
            };
            _recentSelector.ValueChanged += (_, _) => UpdateVisibility();
            _recentSelector.Accepted += (_, _) => Confirmed?.Invoke(this, EventArgs.Empty);
            _urlField.Accepted += (_, _) => Confirmed?.Invoke(this, EventArgs.Empty);
            Add(label, _recentSelector, _urlField);
            UpdateVisibility();
        }
        else
        {
            var label = new Label { Text = "Repository URL:" };
            _urlField = new TextField { Y = Pos.Bottom(label), Width = Dim.Fill() };
            _urlField.Accepted += (_, _) => Confirmed?.Invoke(this, EventArgs.Empty);
            Add(label, _urlField);
        }
    }

    private void UpdateVisibility()
    {
        var manual = _recentSelector!.Value == _recentUrls.Count;
        _urlField.Visible = manual;
        if (manual) _urlField.SetFocus(); else _recentSelector.SetFocus();
    }

    /// <summary>Returns the selected/entered URL (trimmed, no trailing slash), or null if empty.</summary>
    public string? GetUrl()
    {
        var url = _recentSelector is not null && _recentSelector.Value != _recentUrls.Count
            ? _recentUrls[_recentSelector.Value!.Value]
            : _urlField.Text.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(url) ? null : url;
    }
}

/// <summary>
/// Edits the agent: a single-select list of built-in agents plus a "Custom agent..." entry that
/// reveals a text field for a free-form agent identifier.
/// </summary>
sealed class AgentEditDialog : FieldDialog<string?>
{
    private readonly OptionSelector _selector;
    private readonly Label _customLabel;
    private readonly TextField _customField;
    private readonly int _customIndex;

    public AgentEditDialog(string currentAgentId) : base("Edit agent")
    {
        Width = 64;
        Height = Dim.Auto();

        var labels = Options.BuiltInAgents.Select(a => a.Label).Append("Custom agent...").ToArray();
        _customIndex = labels.Length - 1;
        var currentIndex = Options.BuiltInAgents.FindIndex(a => a.Id == currentAgentId);

        _selector = new OptionSelector { Labels = labels, Width = Dim.Fill() };
        _selector.Value = currentIndex >= 0 ? currentIndex : _customIndex;

        _customLabel = new Label { Text = "Custom agent id:", Y = Pos.Bottom(_selector) + 1 };
        _customField = new TextField
        {
            Y = Pos.Top(_customLabel),
            X = Pos.Right(_customLabel) + 1,
            Width = Dim.Fill(),
            Text = currentIndex < 0 ? currentAgentId : "",
        };

        _selector.ValueChanged += (_, _) => UpdateCustomFieldVisibility();
        UpdateCustomFieldVisibility();

        Add(_selector, _customLabel, _customField);
        ConfirmOnAccept(_selector);
        ConfirmOnAccept(_customField);
    }

    private void UpdateCustomFieldVisibility()
    {
        var showCustom = _selector.Value == _customIndex;
        _customLabel.Visible = showCustom;
        _customField.Visible = showCustom;
        FocusDeferred(showCustom ? _customField : _selector);
    }

    protected override bool TryApply()
    {
        if (_selector.Value == _customIndex)
        {
            var id = _customField.Text.Trim();
            if (string.IsNullOrEmpty(id))
            {
                FocusDeferred(_customField);
                return false;
            }
            Result = id;
            return true;
        }

        Result = Options.BuiltInAgents[_selector.Value!.Value].Id;
        return true;
    }

    protected override void OnShown() => UpdateCustomFieldVisibility();
}

/// <summary>
/// Edits the sandbox template: whether to use one at all, which source it comes from, and the
/// source-specific details (registry image name / Git repository + Dockerfile / local Dockerfile).
/// </summary>
sealed class TemplateEditDialog : StepFieldDialog<TemplateConfig?>
{
    private readonly TemplateConfig? _original;
    private readonly List<string> _recentUrls;
    private readonly HashSet<string> _fetchedRepos;

    private string _owner = "";
    private string _repo = "";
    private string _repoUrl = "";
    private string _branch = "";
    private string _cloneDir = "";

    public TemplateEditDialog(TemplateConfig? current, List<string> recentUrls, HashSet<string> fetchedRepos)
        : base("Edit template")
    {
        _original = current;
        _recentUrls = recentUrls;
        _fetchedRepos = fetchedRepos;
        Result = current;
        ShowUseCustomStep();
    }

    private void ShowUseCustomStep()
    {
        var selector = new OptionSelector { Labels = ["No custom template", "Use a custom template"], Width = Dim.Fill() };
        selector.Value = _original is null ? 0 : 1;

        SetStep(selector, "Next", () =>
        {
            if (selector.Value == 0)
            {
                Result = null;
                return true;
            }
            ShowSourceStep();
            return false;
        });
    }

    private void ShowSourceStep()
    {
        var selector = new OptionSelector
        {
            Labels = Options.TemplateSources.Select(s => s.DisplayName).ToArray(),
            Width = Dim.Fill(),
        };
        var currentIndex = _original is null ? -1 : Options.TemplateSources.FindIndex(s => s.Source == _original.Source);
        selector.Value = currentIndex >= 0 ? currentIndex : 0;

        SetStep(selector, "Next", () =>
        {
            switch (Options.TemplateSources[selector.Value!.Value].Source)
            {
                case TemplateSource.Registry: ShowRegistryStep(); break;
                case TemplateSource.GitRepo: ShowGitRepoUrlStep(); break;
                case TemplateSource.Local: ShowLocalStep(); break;
            }
            return false;
        });
    }

    private void ShowRegistryStep()
    {
        var label = new Label { Text = "Image name (e.g. ubuntu:22.04):" };
        var field = new TextField
        {
            Y = Pos.Bottom(label),
            Width = Dim.Fill(),
            Text = _original?.Source == TemplateSource.Registry ? _original.ImageName : "",
        };
        var content = Row(label, field);

        SetStep(content, "Save", () =>
        {
            var imageName = field.Text.Trim();
            if (string.IsNullOrEmpty(imageName))
            {
                ShowError("Image name is required.");
                return false;
            }
            Result = new TemplateConfig(TemplateSource.Registry, imageName, null, null);
            return true;
        }, confirmTarget: field);
    }

    private void ShowGitRepoUrlStep()
    {
        var picker = new RepoUrlPicker(_recentUrls);
        picker.Confirmed += (_, _) => InvokeCommand(TuiCommand.Accept);
        SetStep(picker, "Next", () =>
        {
            var url = picker.GetUrl();
            if (url is null)
            {
                ShowError("Enter a repository URL.");
                return false;
            }
            var (owner, repo) = RepoTools.ParseGitHubUrl(url);
            if (owner is null || repo is null)
            {
                ShowError("Invalid GitHub repository URL. Expected format: https://github.com/owner/repo");
                return false;
            }
            _repoUrl = url;
            _owner = owner;
            _repo = repo;
            ShowGitRepoBranchStep();
            return false;
        });
    }

    private void ShowGitRepoBranchStep()
    {
        var label = new Label { Text = "Branch (leave blank for default):" };
        var field = new TextField { Y = Pos.Bottom(label), Width = Dim.Fill(), Text = _branch };
        var content = Row(label, field);

        SetStep(content, "Fetch", () =>
        {
            _branch = field.Text.Trim();
            StartFetch();
            return false;
        }, confirmTarget: field);
    }

    private void StartFetch()
    {
        var app = App!;
        SetStep(new Label { Text = "Fetching repository..." }, "Fetch", () => false);
        SetOkEnabled(false);

        Task.Run(async () =>
        {
            Exception? error = null;
            List<string> dockerfiles = [];
            var cloneDir = "";
            try
            {
                RepoTools.AddRecentUrl(_recentUrls, _repoUrl);
                cloneDir = await RepoTools.EnsureRepo(_owner, _repo, _branch, _fetchedRepos);
                dockerfiles = RepoTools.FindDockerfiles(cloneDir);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            app.Invoke(() =>
            {
                if (IsClosed) return;
                SetOkEnabled(true);
                if (error is not null)
                {
                    ShowGitRepoBranchStep();
                    ShowError($"Failed to fetch repository: {error.Message}");
                    return;
                }
                if (dockerfiles.Count == 0)
                {
                    ShowGitRepoBranchStep();
                    ShowError("No Dockerfiles found in the repository.");
                    return;
                }
                _cloneDir = cloneDir;
                ShowGitRepoDockerfileStep(dockerfiles);
            });
        });
    }

    private void ShowGitRepoDockerfileStep(List<string> dockerfiles)
    {
        ObservableCollection<string> items = [.. dockerfiles];
        var listView = new ListView<string> { Width = Dim.Fill(), Height = Dim.Auto(minimumContentDim: 1, maximumContentDim: 8) };
        listView.SetSource(items);

        var label = new Label { Text = "Select a Dockerfile:" };
        listView.Y = Pos.Bottom(label);
        var content = Row(label, listView);

        SetStep(content, "Select", () =>
        {
            if (listView.Index is not int index)
            {
                ShowError("Select a Dockerfile.");
                return false;
            }
            var dockerfilePath = dockerfiles[index];
            var absolutePath = Path.Combine(_cloneDir, dockerfilePath);
            var imageName = $"create-sbx-{Guid.NewGuid():N}"[..21];
            Result = new TemplateConfig(TemplateSource.GitRepo, imageName, absolutePath, _cloneDir, _branch);
            return true;
        }, confirmTarget: listView);
    }

    private void ShowLocalStep()
    {
        var label = new Label { Text = "Path to Dockerfile:" };
        var field = new TextField
        {
            Y = Pos.Bottom(label),
            Width = Dim.Fill(),
            Text = _original?.Source == TemplateSource.Local ? _original.DockerfilePath ?? "" : "",
        };
        var content = Row(label, field);

        SetStep(content, "Save", () =>
        {
            var dockerfilePath = Path.GetFullPath(field.Text.Trim());
            if (!File.Exists(dockerfilePath))
            {
                ShowError($"Dockerfile not found: {dockerfilePath}");
                return false;
            }
            var context = Path.GetDirectoryName(dockerfilePath)!;
            var imageName = $"create-sbx-{Guid.NewGuid():N}"[..21];
            Result = new TemplateConfig(TemplateSource.Local, imageName, dockerfilePath, context);
            return true;
        }, confirmTarget: field);
    }
}

/// <summary>
/// Adds one or more kits from a Git repository: pick the repository/branch, fetch it, then
/// multi-select the kits to add.
/// </summary>
sealed class AddKitDialog : StepFieldDialog<List<SelectedKit>?>
{
    private readonly List<string> _recentUrls;
    private readonly HashSet<string> _fetchedRepos;

    private string _owner = "";
    private string _repo = "";
    private string _repoUrl = "";
    private string _branch = "";

    public AddKitDialog(List<string> recentUrls, HashSet<string> fetchedRepos) : base("Add kit")
    {
        _recentUrls = recentUrls;
        _fetchedRepos = fetchedRepos;
        ShowRepoUrlStep();
    }

    private void ShowRepoUrlStep()
    {
        var picker = new RepoUrlPicker(_recentUrls);
        picker.Confirmed += (_, _) => InvokeCommand(TuiCommand.Accept);
        SetStep(picker, "Next", () =>
        {
            var url = picker.GetUrl();
            if (url is null)
            {
                ShowError("Enter a repository URL.");
                return false;
            }
            var (owner, repo) = RepoTools.ParseGitHubUrl(url);
            if (owner is null || repo is null)
            {
                ShowError("Invalid GitHub repository URL. Expected format: https://github.com/owner/repo");
                return false;
            }
            _repoUrl = url;
            _owner = owner;
            _repo = repo;
            ShowBranchStep();
            return false;
        });
    }

    private void ShowBranchStep()
    {
        var label = new Label { Text = "Branch (leave blank for default):" };
        var field = new TextField { Y = Pos.Bottom(label), Width = Dim.Fill(), Text = _branch };
        var content = Row(label, field);

        SetStep(content, "Fetch", () =>
        {
            _branch = field.Text.Trim();
            StartFetch();
            return false;
        }, confirmTarget: field);
    }

    private void StartFetch()
    {
        var app = App!;
        SetStep(new Label { Text = "Fetching kits..." }, "Fetch", () => false);
        SetOkEnabled(false);

        Task.Run(async () =>
        {
            Exception? error = null;
            List<Kit> kits = [];
            try
            {
                RepoTools.AddRecentUrl(_recentUrls, _repoUrl);
                var cloneDir = await RepoTools.EnsureRepo(_owner, _repo, _branch, _fetchedRepos);
                kits = RepoTools.FindKits(cloneDir);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            app.Invoke(() =>
            {
                if (IsClosed) return;
                SetOkEnabled(true);
                if (error is not null)
                {
                    ShowBranchStep();
                    ShowError($"Failed to fetch repository: {error.Message}");
                    return;
                }
                if (kits.Count == 0)
                {
                    ShowBranchStep();
                    ShowError("No kits found in the repository.");
                    return;
                }
                ShowSelectKitsStep(kits);
            });
        });
    }

    private void ShowSelectKitsStep(List<Kit> kits)
    {
        ObservableCollection<string> items = [.. kits.Select(k => k.Label)];
        var listView = new ListView<string>
        {
            Width = Dim.Fill(),
            Height = Dim.Auto(minimumContentDim: 1, maximumContentDim: 8),
            MarkMultiple = true,
            ShowMarks = true,
        };
        listView.SetSource(items);

        var label = new Label { Text = "Select kits to add (space to mark):" };
        listView.Y = Pos.Bottom(label);
        var content = Row(label, listView);

        SetStep(content, "Add", () =>
        {
            var markedIndexes = listView.GetAllMarkedItems().ToList();
            if (markedIndexes.Count == 0)
            {
                ShowError("Select at least one kit.");
                return false;
            }

            var gitUrl = $"git+https://github.com/{_owner}/{_repo}.git";
            var refFragment = string.IsNullOrEmpty(_branch) ? "" : $"&ref={Uri.EscapeDataString(_branch)}";
            Result = markedIndexes.Select(i =>
            {
                var kit = kits[i];
                var url = kit.Directory is not null
                    ? $"{gitUrl}#dir={kit.Directory}{refFragment}"
                    : string.IsNullOrEmpty(refFragment) ? gitUrl : $"{gitUrl}#{refFragment.TrimStart('&')}";
                return new SelectedKit(url, kit.Label);
            }).ToList();
            return true;
        }, confirmOnEnter: false);
    }
}

// ---------------------------------------------------------------------------
// Main view: field rows, buttons, command preview and shortcuts bar
// ---------------------------------------------------------------------------

/// <summary>Implemented by every focusable field row so <see cref="MainView"/> can drive the shortcuts bar uniformly.</summary>
interface IFieldRow
{
    Shortcut[] GetShortcuts();
}

/// <summary>
/// A single "Label: value" row. Enter opens a popup (built by <paramref name="edit"/>) to change the
/// value; the value is only updated when the popup reports it was applied.
/// </summary>
sealed class EditableField<T> : View, IFieldRow
{
    private readonly string _label;
    private readonly Func<T, string> _formatter;
    private readonly Func<IApplication, T, (bool Applied, T? Value)> _edit;

    public T Value { get; private set; }
    public event EventHandler? Changed;

    public EditableField(string label, T initialValue, Func<T, string> formatter, Func<IApplication, T, (bool Applied, T? Value)> edit)
    {
        _label = label;
        Value = initialValue;
        _formatter = formatter;
        _edit = edit;

        CanFocus = true;
        Width = Dim.Fill();
        Height = 1;

        AddCommand(TuiCommand.Accept, () => { Open(); return true; });

        Render();
    }

    public Shortcut[] GetShortcuts() => [new() { Key = Key.Enter, Text = "Edit" }];

    private void Render() => Text = $"{_label}: {_formatter(Value)}";

    private void Open()
    {
        if (App is null) return;
        var (applied, newValue) = _edit(App, Value);
        if (!applied) return;
        Value = newValue!;
        Render();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The kits row: a list of already-added kits plus "a" (add, via <see cref="AddKitDialog"/>) and "d"
/// (remove the highlighted kit immediately, no popup) shortcuts.
/// </summary>
sealed class KitsField : View, IFieldRow
{
    private readonly List<SelectedKit> _kits;
    private readonly Func<IApplication, List<SelectedKit>?> _addKits;
    private readonly ListView<string> _listView;
    private readonly ObservableCollection<string> _items = [];

    public event EventHandler? Changed;

    public KitsField(List<SelectedKit> kits, Func<IApplication, List<SelectedKit>?> addKits)
    {
        _kits = kits;
        _addKits = addKits;

        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Auto();

        var label = new Label { Text = "Kits:" };
        _listView = new ListView<string>
        {
            Y = Pos.Bottom(label),
            Width = Dim.Fill(),
            Height = Dim.Auto(minimumContentDim: 1, maximumContentDim: 5),
        };
        RefreshItems();

        _listView.KeyDown += OnKeyDown;
        Add(label, _listView);
    }

    public Shortcut[] GetShortcuts() =>
        _kits.Count > 0 && _listView.Index is int
            ? [new() { Key = Key.A, Text = "Add kit" }, new() { Key = Key.D, Text = "Remove kit" }]
            : [new() { Key = Key.A, Text = "Add kit" }];

    private void OnKeyDown(object? sender, Key key)
    {
        if (key == Key.A)
        {
            AddKit();
            key.Handled = true;
        }
        else if (key == Key.D && _kits.Count > 0 && _listView.Index is int index)
        {
            RemoveAt(index);
            key.Handled = true;
        }
    }

    private void AddKit()
    {
        if (App is null) return;
        var added = _addKits(App);
        if (added is not { Count: > 0 }) return;
        _kits.AddRange(added);
        RefreshItems();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveAt(int index)
    {
        if (index >= _kits.Count) return;
        _kits.RemoveAt(index);
        RefreshItems();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshItems()
    {
        _items.Clear();
        foreach (var item in _kits.Count > 0 ? _kits.Select(k => k.DisplayLabel) : ["(none)"])
            _items.Add(item);
        _listView.SetSource(_items);
    }
}

/// <summary>
/// The inline form: fields in the same order as the original prompt flow, Create/Exit buttons, a
/// live command preview, and a shortcuts bar that reflects whichever field currently has focus.
/// </summary>
sealed class MainView : Runnable<MainAction>
{
    private readonly SandboxFormState _state;
    private readonly List<string> _recentUrls = RepoTools.LoadRecentUrls();
    private readonly HashSet<string> _fetchedRepos = [];
    private readonly Label _preview;
    private readonly StatusBar _statusBar;

    public MainView(SandboxFormState state)
    {
        _state = state;
        Width = Dim.Fill();
        Height = Dim.Auto();

        var nameField = new EditableField<string>(
            "Name", state.Name, v => v,
            (app, current) =>
            {
                var result = EditText(app, "Edit sandbox name", current);
                return (result is not null, result ?? current);
            });

        var agentField = new EditableField<string>(
            "Agent", state.AgentId, FormatAgent,
            (app, current) =>
            {
                var dlg = new AgentEditDialog(current);
                app.Run(dlg);
                return (dlg.Applied, dlg.Applied ? dlg.Result! : current);
            });

        var workDirField = new EditableField<string>(
            "Working directory", state.WorkDir, v => v,
            (app, current) =>
            {
                var result = EditText(app, "Edit working directory", current);
                return (result is not null, result ?? current);
            });

        var workspaceModeField = new EditableField<WorkspaceMode>(
            "Workspace mode", state.WorkspaceMode, m => m.Name,
            (app, current) =>
            {
                var selector = new OptionSelector { Labels = Options.WorkspaceModes.Select(m => m.Label).ToArray(), Width = Dim.Fill() };
                var currentIndex = Options.WorkspaceModes.IndexOf(current);
                selector.Value = currentIndex >= 0 ? currentIndex : 0;
                var prompt = new Prompt<OptionSelector, int?>(selector) { Title = "Select workspace mode", Width = 70 };
                app.Run(prompt);
                return (prompt.Result is int, prompt.Result is int idx ? Options.WorkspaceModes[idx] : current);
            });

        var templateField = new EditableField<TemplateConfig?>(
            "Template", state.Template, FormatTemplate,
            (app, current) =>
            {
                var dlg = new TemplateEditDialog(current, _recentUrls, _fetchedRepos);
                app.Run(dlg);
                return (dlg.Applied, dlg.Applied ? dlg.Result : current);
            });

        var kitsField = new KitsField(state.Kits, app =>
        {
            var dlg = new AddKitDialog(_recentUrls, _fetchedRepos);
            app.Run(dlg);
            return dlg.Applied ? dlg.Result : null;
        });

        var createButton = new Button { Text = "Create Sandbox", IsDefault = true };
        var exitButton = new Button { Text = "Exit", X = Pos.Right(createButton) + 2 };
        createButton.Accepted += (_, _) => { Result = MainAction.Create; App!.RequestStop(); };
        exitButton.Accepted += (_, _) => { Result = MainAction.Exit; App!.RequestStop(); };
        var buttonRow = new View { Width = Dim.Fill(), Height = 1 };
        buttonRow.Add(createButton, exitButton);

        _preview = new Label { Width = Dim.Fill(), Height = Dim.Auto() };
        _statusBar = new StatusBar();

        nameField.Y = 0;
        agentField.Y = Pos.Bottom(nameField);
        workDirField.Y = Pos.Bottom(agentField);
        workspaceModeField.Y = Pos.Bottom(workDirField);
        templateField.Y = Pos.Bottom(workspaceModeField);
        kitsField.Y = Pos.Bottom(templateField) + 1;
        buttonRow.Y = Pos.Bottom(kitsField) + 1;
        _preview.Y = Pos.Bottom(buttonRow) + 1;
        _statusBar.Y = Pos.Bottom(_preview) + 1;

        Add(nameField, agentField, workDirField, workspaceModeField, templateField, kitsField, buttonRow, _preview, _statusBar);

        nameField.Changed += (_, _) => { state.Name = nameField.Value; RefreshPreview(); };
        agentField.Changed += (_, _) => { state.AgentId = agentField.Value; RefreshPreview(); };
        workDirField.Changed += (_, _) => { state.WorkDir = workDirField.Value; RefreshPreview(); };
        workspaceModeField.Changed += (_, _) => { state.WorkspaceMode = workspaceModeField.Value; RefreshPreview(); };
        templateField.Changed += (_, _) => { state.Template = templateField.Value; RefreshPreview(); };
        kitsField.Changed += (_, _) => RefreshPreview();

        foreach (var row in new View[] { nameField, agentField, workDirField, workspaceModeField, templateField, kitsField })
        {
            var provider = (IFieldRow)row;
            row.HasFocusChanged += (_, _) =>
            {
                if (row.HasFocus) UpdateShortcuts(provider.GetShortcuts());
            };
        }

        RefreshPreview();
    }

    private static string FormatAgent(string id)
    {
        var agent = Options.BuiltInAgents.FirstOrDefault(a => a.Id == id);
        return agent is not null ? $"{agent.DisplayName} ({agent.Id})" : id;
    }

    private static string FormatTemplate(TemplateConfig? template) => template switch
    {
        null => "None",
        { Source: TemplateSource.Registry } => template.ImageName,
        { Source: TemplateSource.GitRepo } => $"{Path.GetFileName(template.DockerfilePath)} (git repository)",
        { Source: TemplateSource.Local } => $"{template.DockerfilePath} (local)",
        _ => "None",
    };

    private static string? EditText(IApplication app, string title, string currentValue)
    {
        TextField tf = new() { Text = currentValue, Width = Dim.Fill() };
        var prompt = new Prompt<TextField, string>(tf) { Title = title, Width = 60 };
        app.Run(prompt);
        return prompt.Result;
    }

    private void UpdateShortcuts(Shortcut[] shortcuts)
    {
        _statusBar.RemoveAll();
        _statusBar.Add(shortcuts);
    }

    private void RefreshPreview()
    {
        var lines = new List<string>();
        if (_state.Template?.Source is TemplateSource.GitRepo or TemplateSource.Local)
            lines.Add($"The Dockerfile {_state.Template.DockerfilePath} will be built before creating the sandbox.");

        var displayTemplateName = _state.Template?.Source is TemplateSource.GitRepo or TemplateSource.Local
            ? "<image-id>"
            : _state.Template?.ImageName;

        lines.Add("Command:");
        lines.Add(RepoTools.BuildDisplayCommand(_state.Name, displayTemplateName, _state.Kits, _state.WorkspaceMode, _state.AgentId, _state.WorkDir));

        _preview.Text = string.Join('\n', lines);
    }
}
