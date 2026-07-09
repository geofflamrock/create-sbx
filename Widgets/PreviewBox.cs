using CreateSbx.Models;
using CreateSbx.Services;

namespace CreateSbx.Widgets;

/// <summary>The always-visible preview: the live `sbx create ...` command, in a lighter-bordered
/// box. Reads straight from <see cref="SandboxConfig"/> so every screen — the main screen and
/// every field editor — can show the exact same preview. The actual build/create output has its
/// own dedicated screen (<c>CreateScreen</c>) rather than showing up here.</summary>
internal sealed class PreviewBox : IWidget
{
    private readonly SandboxConfig _config;

    public PreviewBox(SandboxConfig config)
    {
        _config = config;
    }

    public int MeasureHeight() => 3;

    public void Render(RenderContext context)
    {
        var command = SbxCommandBuilder.BuildDisplayCommand(_config, SbxCommandBuilder.GetDisplayTemplateName(_config.Template));

        context.Render(
            new BoxWidget()
                .Border(Border.Rounded)
                .Style(Color.Grey)
                .TitlePadding(1)
                .MarkupTitle("[bold]Preview[/]")
                .Inner(new PaddingWidget(
                    new Padding(1, 0),
                    Paragraph.FromMarkup($"[blue]{MarkupText.Escape(command)}[/]").Ellipsis())),
            context.Viewport);
    }
}
