using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Pushed the moment "Create sandbox" is activated, replacing the field list entirely:
/// starts the build/create job and streams its output live, with no help bar since there's
/// nothing to do but watch. Once the job finishes, shows a success/failure banner and stays up
/// (dismissed only by the user) — an inline-mode app has no alt-screen to fall back to, so
/// leaving right away would take the log with it.</summary>
internal sealed class CreateScreen : Screen
{
    private static readonly KeyBinding DismissBinding = KeyBinding.Combine(
        KeyBinding.For(Key.Enter), KeyBinding.For(Key.Escape), KeyBinding.For('q')).WithHelp("Exit");

    private readonly SandboxConfig _config;
    private readonly List<string> _log = [];
    private readonly ScrollViewWidget _scroller = new ScrollViewWidget().HorizontalScroll(ScrollMode.Disabled);
    private bool _finished;
    private int _exitCode;

    public CreateScreen(SandboxConfig config)
    {
        _config = config;
    }

    public override void OnEnter(ApplicationContext context)
    {
        var template = _config.Template;

        context.StartJob(async job =>
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

    public override void OnMessage(ApplicationContext context, ApplicationMessage message)
    {
        switch (message)
        {
            case LogMessage log:
                _log.Add(log.Text);
                return;
            case SbxProcessFinishedMessage finished:
                _finished = true;
                _exitCode = finished.ExitCode;
                return;
            case JobFailedMessage failed:
                _log.Add($"Error: {failed.Exception.Message}");
                return;
        }

        if (message is not KeyMessage key)
        {
            return;
        }

        if (_finished && DismissBinding.Matches(key))
        {
            context.Quit();
            return;
        }

        _scroller.KeyMap.HandleKey(key);
    }

    public override void Render(RenderContext context)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Banner").Size(1),
                new Layout("Log"),
                new Layout("Footer").Size(1));

        var banner = _finished
            ? _exitCode == 0
                ? "[green bold]Sandbox created successfully.[/]"
                : $"[red bold]sbx exited with code {_exitCode}.[/]"
            : $"[cyan]Creating sandbox {MarkupText.Escape(_config.Name)}...[/]";

        context.Render(Paragraph.FromMarkup(banner), layout.GetArea(context, "Banner"));

        _scroller.Inner(Paragraph.FromMarkup(string.Join("\n", _log.Select(MarkupText.Escape))));
        _scroller.ScrollToBottom();
        context.Render(_scroller, layout.GetArea(context, "Log"));

        if (_finished)
        {
            context.Render(
                Paragraph.FromMarkup("[grey]↑/↓ scroll  [[Enter/Esc/q]] exit[/]"),
                layout.GetArea(context, "Footer"));
        }
    }
}
