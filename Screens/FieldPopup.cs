namespace CreateSbx.Screens;

/// <summary>Wraps a field's editor <see cref="Screen"/> in a centered popup box. Ports
/// <c>Sandbox/Widgets/PopupWidget.cs</c> from the spectre.tui sample.</summary>
internal sealed class FieldPopup : Screen
{
    private readonly Size _size;
    private readonly string _title;
    private readonly Screen _inner;

    public override bool IsTransparent => true;

    public FieldPopup(Size size, string title, Screen inner)
    {
        _size = size;
        _title = title;
        _inner = inner;
    }

    public override void OnEnter(ApplicationContext context) => _inner.OnEnter(context);

    public override void OnLeave(ApplicationContext context) => _inner.OnLeave(context);

    public override void OnMessage(ApplicationContext context, ApplicationMessage message) =>
        _inner.OnMessage(context, message);

    public override void Update(FrameInfo frame, IRenderBounds bounds) => _inner.Update(frame, bounds);

    public override void Render(RenderContext context)
    {
        context.Render(
            new PopupWidget(_size)
                .Content(
                    new BoxWidget()
                        .Border(Border.Rounded)
                        .Title(_title, TitlePosition.Top, Justify.Center)
                        .Style(Color.Yellow)
                        .Inner(_inner)));
    }
}
