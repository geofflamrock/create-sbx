namespace CreateSbx.Screens;

/// <summary>A step within a multi-step field screen. Unlike <see cref="Screen"/>, a step is never
/// pushed onto the application's screen stack itself — a composite field screen (e.g.
/// <c>TemplateFieldScreen</c>) holds a reference to whichever step is current and manually
/// forwards <see cref="OnMessage"/>/render/help to it, swapping the reference to move between
/// steps without disturbing the screen managing the whole field. Extending <see cref="IWidget"/>
/// and <see cref="IKeyMap"/> lets a composite render and describe the current step generically,
/// without a big switch over every possible step type.</summary>
internal interface IStep : IWidget, IKeyMap
{
    void OnMessage(ApplicationContext context, ApplicationMessage message);
}
