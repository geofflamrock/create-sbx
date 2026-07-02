using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

internal sealed class MainScreen : Screen
{
    private const int MaxLogLines = 500;

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
        _fields = new ListWidget<FieldListItem>(BuildRows())
            .HighlightSymbol("→ ")
            .HighlightStyle(new Style(decoration: Decoration.Bold))
            .WrapAround()
            .SelectedIndex(0);

        _previewContent = new PreviewContent(
            () => SbxCommandBuilder.BuildDisplayCommand(_config, SbxCommandBuilder.GetDisplayTemplateName(_config.Template)),
            _logScroller);

        _layout = new Layout("Root")
            .SplitRows(
                new Layout("Fields"),
                new Layout("Preview").Size(10),
                new Layout("Help").Size(1));
    }

    private List<FieldListItem> BuildRows() =>
    [
        new(FieldId.Name, "Name", () => _config.Name),
        new(FieldId.Agent, "Agent", () => _config.AgentId),
        new(FieldId.WorkDir, "Working directory", () => _config.WorkDir),
        new(FieldId.WorkspaceMode, "Workspace mode", () => _config.WorkspaceMode.Name),
        new(FieldId.Template, "Template", DescribeTemplate),
        new(FieldId.Kits, "Kits", DescribeKits),
        new(FieldId.Create, "Create sandbox", null),
        new(FieldId.Exit, "Exit", null),
    ];

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

            _fields.KeyMap.HandleKey(key);
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
                context.Push(new FieldPopup(new Size(50, 6), "Name",
                    new TextFieldEditorScreen("Sandbox name", _config.Name, (ctx, value) =>
                    {
                        _config.Name = value;
                        ctx.Pop();
                    })));
                break;

            case FieldId.Agent:
                context.Push(new FieldPopup(new Size(60, 14), "Agent",
                    new AgentFieldScreen(_config.AgentId, (ctx, id) =>
                    {
                        _config.AgentId = id;
                        ctx.Pop();
                    })));
                break;

            case FieldId.WorkDir:
                context.Push(new FieldPopup(new Size(50, 6), "Working directory",
                    new TextFieldEditorScreen("Working directory", _config.WorkDir, (ctx, value) =>
                    {
                        _config.WorkDir = value;
                        ctx.Pop();
                    })));
                break;

            case FieldId.WorkspaceMode:
                context.Push(new FieldPopup(new Size(60, 8), "Workspace mode",
                    new SingleSelectEditorScreen<WorkspaceModeOption>(
                        "Select workspace mode",
                        SandboxConfig.WorkspaceModes,
                        m => $"{m.Name} [grey]- {MarkupText.Escape(m.Description)}[/]",
                        (ctx, mode) =>
                        {
                            _config.WorkspaceMode = mode;
                            ctx.Pop();
                        },
                        SandboxConfig.WorkspaceModes.ToList().IndexOf(_config.WorkspaceMode))));
                break;

            case FieldId.Template:
                context.Push(new FieldPopup(new Size(70, 16), "Template",
                    new TemplateFieldScreen(_config, (ctx, template) =>
                    {
                        _config.Template = template;
                        ctx.Pop();
                    })));
                break;

            case FieldId.Kits:
                context.Push(new FieldPopup(new Size(70, 16), "Kits", new KitsFieldScreen(_config)));
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
        context.Render(
            new BoxWidget().Border(Border.Rounded).TitlePadding(1).MarkupTitle("[bold]create-sbx[/]").Inner(_fields),
            _layout.GetArea(context, "Fields"));

        context.Render(
            new BoxWidget().Border(Border.Rounded).TitlePadding(1).MarkupTitle("[bold]Preview[/]").Inner(_previewContent),
            _layout.GetArea(context, "Preview"));

        context.Render(new HelpWidget(_keyMap, _fields.KeyMap), _layout.GetArea(context, "Help"));
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
