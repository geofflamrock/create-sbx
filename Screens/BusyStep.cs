using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Shown while a background job (git fetch, etc.) is running. Escape still pops the
/// popup — the owning composite screen cancels the job via <c>OnLeave</c>.</summary>
internal sealed class BusyStep : IStep
{
    private readonly SpinnerWidget _spinner = new SpinnerWidget().Kind(SpinnerKind.Default);
    private readonly string _text;

    public BusyStep(string text)
    {
        _text = text;
    }

    public void Update(FrameInfo frame) => _spinner.Update(frame);

    public void OnMessage(ApplicationContext context, ApplicationMessage message)
    {
        if (message is KeyMessage { Key: Key.Escape })
        {
            context.Pop();
        }
    }

    public void Render(RenderContext context)
    {
        var layout = new Layout("Root").SplitColumns(new Layout("Spinner").Size(2), new Layout("Text"));
        context.Render(_spinner, layout.GetArea(context, "Spinner"));
        context.Render(Paragraph.FromMarkup($"[grey]{MarkupText.Escape(_text)}[/]"), layout.GetArea(context, "Text"));
    }
}
