using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Pushed the moment "Create sandbox" is activated, replacing the field list entirely:
/// starts the build/create job and streams its output live, with no help bar since there's
/// nothing to do but watch. Once the job finishes, shows a success/failure message in place of
/// the help bar — on success, Enter exits; on failure, Enter goes back to the main screen (to fix
/// something and retry) or Esc exits.</summary>
internal sealed class CreateScreen : Screen
{
    private const int MaxVisibleLogLines = 20;

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

        if (_finished)
        {
            if (key.Key == Key.Enter)
            {
                if (_exitCode == 0)
                {
                    context.Quit();
                }
                else
                {
                    context.Pop();
                }

                return;
            }

            if (_exitCode != 0 && key.Key == Key.Escape)
            {
                context.Quit();
                return;
            }
        }

        _scroller.KeyMap.HandleKey(key);
    }

    public override void Render(RenderContext context)
    {
        _scroller.Inner(Paragraph.FromMarkup(string.Join("\n", _log.Select(MarkupText.Escape))));
        _scroller.ScrollToBottom();

        if (!_finished)
        {
            context.Render(_scroller, context.Viewport);
            return;
        }

        // Size the log to its own content (capped so a huge log can't push the message off
        // screen) rather than letting it fill the whole viewport, so the message sits right
        // underneath the output instead of pinned to the bottom of an otherwise-empty screen.
        var logHeight = Math.Clamp(_log.Count, 1, MaxVisibleLogLines);
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Log").Size(logHeight),
                new Layout("Spacer").Size(1),
                new Layout("Message").Size(2),
                new Layout("Filler"));

        context.Render(_scroller, layout.GetArea(context, "Log"));

        var message = _exitCode == 0
            ? "[green]Sandbox created successfully. Press Enter to exit.[/]"
            : "[red]There was an error creating the sandbox. Press Enter to go back to the main screen, or Esc to exit.[/]";

        context.Render(Paragraph.FromMarkup(message), layout.GetArea(context, "Message"));
    }
}
