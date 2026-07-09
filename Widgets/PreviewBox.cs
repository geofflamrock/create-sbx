using CreateSbx.Models;
using CreateSbx.Services;

namespace CreateSbx.Widgets;

/// <summary>The always-visible preview: the live `sbx create ...` command plus the build/create
/// log, in a lighter-bordered box. Reads straight from <see cref="SandboxConfig"/> so every
/// screen — the main screen and every field editor — can show the exact same preview.</summary>
internal sealed class PreviewBox : IWidget
{
    public const int MaxVisibleLogLines = 8;

    private readonly SandboxConfig _config;
    private readonly ScrollViewWidget _logScroller = new ScrollViewWidget().HorizontalScroll(ScrollMode.Disabled);
    private IWidget _logContent = Paragraph.FromMarkup("");
    private int _renderedLogCount = -1;

    public PreviewBox(SandboxConfig config)
    {
        _config = config;
    }

    public int MeasureHeight() => 2 + 1 + Math.Clamp(_config.Log.Count, 0, MaxVisibleLogLines);

    public void Render(RenderContext context)
    {
        if (_renderedLogCount != _config.Log.Count)
        {
            _logContent = Paragraph.FromMarkup(string.Join("\n", _config.Log.Select(MarkupText.Escape)));
            _logScroller.Inner(_logContent);
            _logScroller.ScrollToBottom();
            _renderedLogCount = _config.Log.Count;
        }

        context.Render(
            new BoxWidget()
                .Border(Border.Rounded)
                .Style(Color.Grey)
                .TitlePadding(1)
                .MarkupTitle("[bold]Preview[/]")
                .Inner(new PaddingWidget(new Padding(1, 0), new Inner(_config, _logScroller))),
            context.Viewport);
    }

    private sealed class Inner(SandboxConfig config, ScrollViewWidget logScroller) : IWidget
    {
        public void Render(RenderContext context)
        {
            var layout = new Layout("Preview").SplitRows(new Layout("Command").Size(1), new Layout("Log"));
            var command = SbxCommandBuilder.BuildDisplayCommand(config, SbxCommandBuilder.GetDisplayTemplateName(config.Template));

            context.Render(
                Paragraph.FromMarkup($"[blue]{MarkupText.Escape(command)}[/]").Ellipsis(),
                layout.GetArea(context, "Command"));

            context.Render(logScroller, layout.GetArea(context, "Log"));
        }
    }
}
