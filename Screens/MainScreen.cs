using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

internal sealed class MainScreen : ShellScreen
{
    private const int MaxLogLines = 500;

    private readonly MainScreenKeyMap _keyMap = new();
    private int _selectedIndex;
    private IJobHandle? _createJob;

    public int? ExitCode { get; private set; }

    public MainScreen()
        : base(new SandboxConfig(
            RecentEntriesStore.Load(RecentEntriesStore.UrlsFile),
            RecentEntriesStore.Load(RecentEntriesStore.WorkspaceDirectoriesFile)))
    {
    }

    private List<FieldListItem> BuildRows()
    {
        (FieldId Id, string Label, Func<string> GetValue)[] fields =
        [
            (FieldId.Name, "Name", () => Config.Name),
            (FieldId.Agent, "Agent", () => Config.AgentId),
            (FieldId.WorkDir, "Workspace directory", () => Config.WorkDir),
            (FieldId.WorkspaceMode, "Workspace mode", () => Config.WorkspaceMode.Name),
            (FieldId.Template, "Template", DescribeTemplate),
        ];

        var labelColumnWidth = fields.Max(f => f.Label.Length);

        var rows = fields.Select(f => FieldListItem.Field(f.Id, f.Label, f.GetValue, labelColumnWidth)).ToList();

        for (var i = 0; i < Config.KitGroups.Count; i++)
        {
            rows.Add(FieldListItem.ForKitGroup(Config.KitGroups[i], showLabel: i == 0, labelColumnWidth));
        }

        rows.Add(FieldListItem.AddKitRow(showLabel: Config.KitGroups.Count == 0, labelColumnWidth));

        for (var i = 0; i < Config.AdditionalWorkspaceDirectories.Count; i++)
        {
            rows.Add(FieldListItem.ForWorkspaceDirectory(Config.AdditionalWorkspaceDirectories[i], showLabel: i == 0, labelColumnWidth));
        }

        rows.Add(FieldListItem.AddWorkspaceDirectoryRow(showLabel: Config.AdditionalWorkspaceDirectories.Count == 0, labelColumnWidth));

        rows.Add(FieldListItem.Spacer());
        rows.Add(FieldListItem.Action(FieldId.Create, "Create sandbox"));
        rows.Add(FieldListItem.Action(FieldId.Exit, "Exit"));

        return rows;
    }

    private string DescribeTemplate()
    {
        var template = Config.Template;
        if (template is null)
        {
            return "(none)";
        }

        return template.Source switch
        {
            TemplateSource.Registry => template.ImageName,
            TemplateSource.GitRepo =>
                $"{template.RepoSlug} — {Path.GetRelativePath(template.DockerContext!, template.DockerfilePath!)} (will be built)",
            TemplateSource.Local => $"{template.DockerfilePath} (will be built)",
            _ => template.ImageName,
        };
    }

    public override void OnMessage(ApplicationContext context, ApplicationMessage message)
    {
        switch (message)
        {
            case LogMessage log:
                AppendLog(log.Text);
                return;
            case SbxProcessFinishedMessage finished:
                ExitCode = finished.ExitCode;
                context.Push(new ResultScreen(Config.Log, finished.ExitCode));
                return;
            case JobFailedMessage failed:
                AppendLog($"Error: {failed.Exception.Message}");
                return;
        }

        if (message is not KeyMessage key)
        {
            return;
        }

        if (_keyMap.Quit.Matches(key))
        {
            context.Quit();
            return;
        }

        var rows = BuildRows();
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, rows.Count - 1));

        if (_keyMap.Select.Matches(key))
        {
            Activate(context, rows[_selectedIndex]);
            return;
        }

        if (_keyMap.Remove.Matches(key))
        {
            RemoveSelected(rows[_selectedIndex]);
            return;
        }

        if (_keyMap.MoveDown.Matches(key))
        {
            MoveSelection(rows, forward: true);
            return;
        }

        if (_keyMap.MoveUp.Matches(key))
        {
            MoveSelection(rows, forward: false);
        }
    }

    private void RemoveSelected(FieldListItem selected)
    {
        switch (selected)
        {
            case { Id: FieldId.KitGroup, Group: { } group }:
                Config.KitGroups.Remove(group);
                break;
            case { Id: FieldId.WorkspaceDirEntry, Directory: { } directory }:
                Config.AdditionalWorkspaceDirectories.Remove(directory);
                break;
        }
    }

    private void MoveSelection(List<FieldListItem> rows, bool forward)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var step = forward ? 1 : -1;
        do
        {
            _selectedIndex = ((_selectedIndex + step) % rows.Count + rows.Count) % rows.Count;
        }
        while (rows[_selectedIndex].Id == FieldId.Spacer);
    }

    private void Activate(ApplicationContext context, FieldListItem selected)
    {
        switch (selected.Id)
        {
            case FieldId.Name:
                context.Push(new SimpleFieldScreen(Config, new TextFieldEditorScreen("Sandbox name", Config.Name, (ctx, value) =>
                {
                    Config.Name = value;
                    ctx.Pop();
                })));
                break;

            case FieldId.Agent:
                context.Push(new AgentFieldScreen(Config, (ctx, id) =>
                {
                    Config.AgentId = id;
                    ctx.Pop();
                }));
                break;

            case FieldId.WorkDir:
                context.Push(new SimpleFieldScreen(Config, new TextFieldEditorScreen("Workspace directory", Config.WorkDir, (ctx, value) =>
                {
                    Config.WorkDir = value;
                    ctx.Pop();
                })));
                break;

            case FieldId.WorkspaceMode:
                context.Push(new SimpleFieldScreen(Config, new SingleSelectEditorScreen<WorkspaceModeOption>(
                    "Select workspace mode",
                    SandboxConfig.WorkspaceModes,
                    m => $"{m.Name} [grey]- {MarkupText.Escape(m.Description)}[/]",
                    (ctx, mode) =>
                    {
                        Config.WorkspaceMode = mode;
                        ctx.Pop();
                    },
                    SandboxConfig.WorkspaceModes.ToList().IndexOf(Config.WorkspaceMode))));
                break;

            case FieldId.Template:
                context.Push(new TemplateFieldScreen(Config, (ctx, template) =>
                {
                    Config.Template = template;
                    ctx.Pop();
                }));
                break;

            case FieldId.KitGroup:
                context.Push(new KitEditScreen(Config, selected.Group));
                break;

            case FieldId.AddKit:
                context.Push(new KitEditScreen(Config, null));
                break;

            case FieldId.WorkspaceDirEntry:
                context.Push(new WorkspaceDirectoryEditScreen(Config, selected.Directory));
                break;

            case FieldId.AddWorkspaceDir:
                context.Push(new WorkspaceDirectoryEditScreen(Config, null));
                break;

            case FieldId.Create:
                StartCreate(context);
                break;

            case FieldId.Exit:
                context.Quit();
                break;
        }
    }

    private void StartCreate(ApplicationContext context)
    {
        if (_createJob is not null)
        {
            return;
        }

        var template = Config.Template;

        _createJob = context.StartJob(async job =>
        {
            try
            {
                string? effectiveTemplateName = template?.Source == TemplateSource.Registry ? template.ImageName : null;

                if (template?.Source is TemplateSource.GitRepo or TemplateSource.Local)
                {
                    effectiveTemplateName = await DockerService.BuildAndLoadDockerImageAsync(
                        template, line => job.Broadcast(new LogMessage(line)));
                }

                var args = SbxCommandBuilder.BuildArgs(Config, effectiveTemplateName);
                job.Broadcast(new LogMessage($"Creating sandbox {Config.Name}..."));
                job.Broadcast(new LogMessage("sbx " + string.Join(' ', args)));

                var exitCode = await ProcessRunner.RunStreamingAsync(
                    "sbx", args, line => job.Broadcast(new LogMessage(line)), throwOnNonZeroExit: false);

                job.Broadcast(new SbxProcessFinishedMessage(exitCode));
            }
            catch (Exception ex)
            {
                job.Broadcast(new LogMessage($"Error: {ex.Message}"));
                job.Broadcast(new SbxProcessFinishedMessage(1));
            }
        });
    }

    private void AppendLog(string text)
    {
        Config.Log.Add(text);
        while (Config.Log.Count > MaxLogLines)
        {
            Config.Log.RemoveAt(0);
        }
    }

    protected override int MiddleHeight => BuildRows().Sum(r => r.CreateText(false).GetHeight());

    protected override IEnumerable<IKeyMap> HelpKeyMaps => [_keyMap];

    protected override void RenderMiddle(RenderContext context, Rectangle area)
    {
        var rows = BuildRows();
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, rows.Count - 1));

        _keyMap.RemoveHelpText = rows.Count == 0
            ? null
            : rows[_selectedIndex].Id switch
            {
                FieldId.KitGroup => "Remove kit",
                FieldId.WorkspaceDirEntry => "Remove directory",
                _ => null,
            };

        var list = new ListWidget<FieldListItem>(rows)
            .HighlightSymbol("→ ")
            .SelectedIndex(rows.Count == 0 ? null : _selectedIndex);

        context.Render(list, area);
    }
}
