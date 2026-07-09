namespace CreateSbx.Screens;

/// <summary>A step within a multi-step field screen. Unlike <see cref="Screen"/>, a step is never
/// pushed onto the application's screen stack itself — a composite field screen (e.g.
/// <c>TemplateFieldScreen</c>) holds a reference to whichever step is current and manually
/// forwards <see cref="OnMessage"/>/<see cref="Render"/> to it, swapping the reference to move
/// between steps without disturbing the screen managing the whole field.</summary>
internal interface IStep
{
    void OnMessage(ApplicationContext context, ApplicationMessage message);

    void Render(RenderContext context);
}
