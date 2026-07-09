using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

internal sealed class MainScreen : ShellScreen
{
    private const int MaxLogLines = 500;

    private readonly ListWidget<FieldListItem> _fields;
    private readonly MainScreenKeyMap _keyMap = new();
    private IJobHandle? _createJob;

    public int? ExitCode { get; private set; }

    public MainScreen()
        : base(new SandboxConfig(RecentUrlsStore.Load()))
    {
        _fields = new ListWidget<FieldListItem>(BuildRows())
            .HighlightSymbol("→ ")
            .HighlightStyle(new Style(decoration: Decoration.Bold))
            .WrapAround()
            .SelectedIndex(0);
    }

    private List<FieldListItem> BuildRows()
    {
        (FieldId Id, string Label, Func<string>? GetValue)[] fields =
        [
            (FieldId.Name, "Name", () => Config.Name),
            (FieldId.Agent, "Agent", () => Config.AgentId),
            (FieldId.WorkDir, "Working directory", () => Config.WorkDir),
            (FieldId.WorkspaceMode, "Workspace mode", () => Config.WorkspaceMode.Name),
            (FieldId.Template, "Template", DescribeTemplate),
            (FieldId.Kits, "Kits", DescribeKits),
        ];

        var labelColumnWidth = fields.Max(f => f.Label.Length);

        return
        [
            .. fields.Select(f => new FieldListItem(f.Id, f.Label, f.GetValue, labelColumnWidth)),
            new(FieldId.Spacer, "", null),
            new(FieldId.Create, "Create sandbox", null),
            new(FieldId.Exit, "Exit", null),
        ];
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

    private string DescribeKits()
    {
        var lines = Config.KitGroups
            .SelectMany(g => g.SelectedKits.Select(k => DescribeKit(g, k)))
            .ToList();

        return lines.Count == 0 ? "(none)" : string.Join("\n", lines);
    }

    private static string DescribeKit(KitGroup group, Kit kit)
    {
        var branchSuffix = string.IsNullOrEmpty(group.Branch) ? "" : $" ({group.Branch})";
        return $"{group.Owner}/{group.Repo}{branchSuffix} — {kit.DisplayName}";
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

        if (message is KeyMessage key)
        {
            if (_keyMap.Quit.Matches(key))
            {
                context.Quit();
                return;
            }

            if (_keyMap.Select.Matches(key))
            {
                Activate(context);
                return;
            }

            if (_fields.KeyMap.MoveDown.Matches(key))
            {
                _fields.MoveDown();
                SkipSpacer(forward: true);
                return;
            }

            if (_fields.KeyMap.MoveUp.Matches(key))
            {
                _fields.MoveUp();
                SkipSpacer(forward: false);
                return;
            }
        }
    }

    private void SkipSpacer(bool forward)
    {
        if (_fields.SelectedItem?.Id != FieldId.Spacer)
        {
            return;
        }

        if (forward)
        {
            _fields.MoveDown();
        }
        else
        {
            _fields.MoveUp();
        }
    }

    private void Activate(ApplicationContext context)
    {
        var selected = _fields.SelectedItem;
        if (selected is null)
        {
            return;
        }

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
                context.Push(new SimpleFieldScreen(Config, new TextFieldEditorScreen("Working directory", Config.WorkDir, (ctx, value) =>
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

            case FieldId.Kits:
                context.Push(new KitsFieldScreen(Config));
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

    protected override int MiddleHeight => _fields.Items.Sum(item => item.CreateText(false).GetHeight());

    protected override IEnumerable<IKeyMap> HelpKeyMaps => [_keyMap, _fields.KeyMap];

    protected override void RenderMiddle(RenderContext context, Rectangle area) => context.Render(_fields, area);
}
