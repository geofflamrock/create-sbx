using CreateSbx.Models;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Shared chrome for every screen in the app: a blank line, the app title, the preview
/// box, a blank line, this screen's own content ("middle"), a blank line, and a left-aligned help
/// bar — with any leftover space as a blank filler at the very bottom. Both <see cref="MainScreen"/>
/// and every field editor (via <see cref="MultiStepScreen"/>) render through this so the title and
/// preview are always visible and the spacing is identical everywhere.</summary>
internal abstract class ShellScreen : Screen
{
    protected SandboxConfig Config { get; }

    private readonly PreviewBox _preview;

    protected ShellScreen(SandboxConfig config)
    {
        Config = config;
        _preview = new PreviewBox(config);
    }

    protected abstract int MiddleHeight { get; }

    protected abstract void RenderMiddle(RenderContext context, Rectangle area);

    protected abstract IEnumerable<IKeyMap> HelpKeyMaps { get; }

    public override void Render(RenderContext context)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("TopSpacer").Size(1),
                new Layout("Title").Size(1),
                new Layout("Preview").Size(_preview.MeasureHeight()),
                new Layout("MidSpacer").Size(1),
                new Layout("Middle").Size(Math.Max(MiddleHeight, 1)),
                new Layout("HelpSpacer").Size(1),
                new Layout("Help").Size(1),
                new Layout("Filler"));

        context.Render(
            Paragraph.FromMarkup("[bold cyan]create-sbx[/] [grey]— create a Docker sandbox[/]"),
            layout.GetArea(context, "Title"));

        context.Render(_preview, layout.GetArea(context, "Preview"));

        RenderMiddle(context, layout.GetArea(context, "Middle"));

        context.Render(new HelpWidget(HelpKeyMaps.ToArray()).LeftAligned(), layout.GetArea(context, "Help"));
    }
}
