using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Shown after "Create sandbox" finishes: the full log plus a success/failure banner.
/// Stays up until the user dismisses it, since an inline-mode app has no alt-screen to fall back
/// to — leaving right away would take the log with it.</summary>
internal sealed class ResultScreen : Screen
{
    private static readonly KeyBinding DismissBinding = KeyBinding.Combine(
        KeyBinding.For(Key.Enter), KeyBinding.For(Key.Escape), KeyBinding.For('q')).WithHelp("Exit");

    private readonly ScrollViewWidget _scroller = new ScrollViewWidget().HorizontalScroll(ScrollMode.Disabled);
    private readonly IWidget _logContent;
    private readonly int _exitCode;

    public ResultScreen(IReadOnlyList<string> log, int exitCode)
    {
        _exitCode = exitCode;
        _logContent = Paragraph.FromMarkup(string.Join("\n", log.Select(MarkupText.Escape)));
        _scroller.Inner(_logContent);
        _scroller.ScrollToBottom();
    }

    public override void OnMessage(ApplicationContext context, ApplicationMessage message)
    {
        if (message is not KeyMessage key)
        {
            return;
        }

        if (DismissBinding.Matches(key))
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

        var banner = _exitCode == 0
            ? "[green bold]Sandbox created successfully.[/]"
            : $"[red bold]sbx exited with code {_exitCode}.[/]";

        context.Render(Paragraph.FromMarkup(banner), layout.GetArea(context, "Banner"));
        context.Render(_scroller, layout.GetArea(context, "Log"));
        context.Render(
            Paragraph.FromMarkup("[grey]↑/↓ scroll  [[Enter/Esc/q]] exit[/]"),
            layout.GetArea(context, "Footer"));
    }
}
