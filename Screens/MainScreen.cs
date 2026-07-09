using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

internal sealed class MainScreen : Screen
{
    private const int MaxLogLines = 500;
    private const int MaxVisibleLogLines = 8;

    private readonly SandboxConfig _config = new(RecentUrlsStore.Load());
    private readonly ListWidget<FieldListItem> _fields;
    private readonly MainScreenKeyMap _keyMap = new();
    private readonly List<string> _log = [];
    private readonly ScrollViewWidget _logScroller = new ScrollViewWidget().HorizontalScroll(ScrollMode.Disabled);
    private readonly PreviewContent _previewContent;
    private readonly Layout _layout;
    private IJobHandle? _createJob;

    public int? ExitCode { get; private set; }

    public MainScreen()
    {
        var rows = BuildRows();
        _fields = new ListWidget<FieldListItem>(rows)
            .HighlightSymbol("→ ")
            .HighlightStyle(new Style(decoration: Decoration.Bold))
            .WrapAround()
            .SelectedIndex(0);

        _previewContent = new PreviewContent(
            () => SbxCommandBuilder.BuildDisplayCommand(_config, SbxCommandBuilder.GetDisplayTemplateName(_config.Template)),
            _logScroller);

        _layout = new Layout("Root")
            .SplitRows(
                new Layout("Fields").Size(rows.Count),
                new Layout("Preview").Size(3),
                new Layout("Filler"),
                new Layout("Help").Size(1));
    }

    private List<FieldListItem> BuildRows()
    {
        (FieldId Id, string Label, Func<string>? GetValue)[] fields =
        [
            (FieldId.Name, "Name", () => _config.Name),
            (FieldId.Agent, "Agent", () => _config.AgentId),
            (FieldId.WorkDir, "Working directory", () => _config.WorkDir),
            (FieldId.WorkspaceMode, "Workspace mode", () => _config.WorkspaceMode.Name),
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
        var template = _config.Template;
        if (template is null)
        {
            return "(none)";
        }

        return template.Source switch
        {
            TemplateSource.Registry => template.ImageName,
            TemplateSource.GitRepo => $"{Path.GetFileName(template.DockerfilePath)} (will be built)",
            TemplateSource.Local => template.DockerfilePath!,
            _ => template.ImageName,
        };
    }

    private string DescribeKits()
    {
        if (_config.KitGroups.Count == 0)
        {
            return "(none)";
        }

        var kitCount = _config.KitGroups.Sum(g => g.SelectedKits.Count);
        var sourceCount = _config.KitGroups.Count;
        return $"{kitCount} kit{(kitCount == 1 ? "" : "s")} from {sourceCount} source{(sourceCount == 1 ? "" : "s")}";
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
                context.Push(new ResultScreen(_log, finished.ExitCode));
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
                context.Push(new TextFieldEditorScreen("Sandbox name", _config.Name, (ctx, value) =>
                {
                    _config.Name = value;
                    ctx.Pop();
                }));
                break;

            case FieldId.Agent:
                context.Push(new AgentFieldScreen(_config.AgentId, (ctx, id) =>
                {
                    _config.AgentId = id;
                    ctx.Pop();
                }));
                break;

            case FieldId.WorkDir:
                context.Push(new TextFieldEditorScreen("Working directory", _config.WorkDir, (ctx, value) =>
                {
                    _config.WorkDir = value;
                    ctx.Pop();
                }));
                break;

            case FieldId.WorkspaceMode:
                context.Push(new SingleSelectEditorScreen<WorkspaceModeOption>(
                    "Select workspace mode",
                    SandboxConfig.WorkspaceModes,
                    m => $"{m.Name} [grey]- {MarkupText.Escape(m.Description)}[/]",
                    (ctx, mode) =>
                    {
                        _config.WorkspaceMode = mode;
                        ctx.Pop();
                    },
                    SandboxConfig.WorkspaceModes.ToList().IndexOf(_config.WorkspaceMode)));
                break;

            case FieldId.Template:
                context.Push(new TemplateFieldScreen(_config, (ctx, template) =>
                {
                    _config.Template = template;
                    ctx.Pop();
                }));
                break;

            case FieldId.Kits:
                context.Push(new KitsFieldScreen(_config));
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

        var template = _config.Template;

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

                var args = SbxCommandBuilder.BuildArgs(_config, effectiveTemplateName);
                job.Broadcast(new LogMessage($"Creating sandbox {_config.Name}..."));
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
        _log.Add(text);
        while (_log.Count > MaxLogLines)
        {
            _log.RemoveAt(0);
        }

        _previewContent.RefreshLog(_log);
    }

    public override void Render(RenderContext context)
    {
        var visibleLogLines = Math.Clamp(_log.Count, 0, MaxVisibleLogLines);
        _layout.GetLayout("Preview").Size(2 + 1 + visibleLogLines);

        context.Render(_fields, _layout.GetArea(context, "Fields"));

        context.Render(
            new BoxWidget()
                .Border(Border.Rounded)
                .TitlePadding(1)
                .MarkupTitle("[bold]Preview[/]")
                .Inner(new PaddingWidget(new Padding(1, 0), _previewContent)),
            _layout.GetArea(context, "Preview"));

        context.Render(new HelpWidget(_keyMap, _fields.KeyMap).LeftAligned(), _layout.GetArea(context, "Help"));
    }

    private sealed class PreviewContent(Func<string> getCommand, ScrollViewWidget logScroller) : IWidget
    {
        private IWidget _logContent = Paragraph.FromMarkup("");

        public void RefreshLog(IReadOnlyList<string> lines)
        {
            _logContent = Paragraph.FromMarkup(string.Join("\n", lines.Select(MarkupText.Escape)));
            logScroller.Inner(_logContent);
            logScroller.ScrollToBottom();
        }

        public void Render(RenderContext context)
        {
            var layout = new Layout("Preview")
                .SplitRows(new Layout("Command").Size(1), new Layout("Log"));

            context.Render(
                Paragraph.FromMarkup($"[blue]{MarkupText.Escape(getCommand())}[/]").Ellipsis(),
                layout.GetArea(context, "Command"));

            logScroller.Inner(_logContent);
            context.Render(logScroller, layout.GetArea(context, "Log"));
        }
    }
}
