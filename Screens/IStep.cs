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
    /// <summary>How many rows this step needs to show its content, used to size the screen's
    /// "middle" area tightly so the help bar sits right beneath it instead of at the bottom of
    /// the screen. An estimate is fine — lists cap it and rely on their own scrolling, and
    /// wrapped text may be approximate.</summary>
    int PreferredHeight { get; }

    void OnMessage(ApplicationContext context, ApplicationMessage message);
}

internal static class StepLayout
{
    /// <summary>Cap on how many rows a list step asks for — beyond this it relies on its own
    /// scrolling rather than pushing the help bar further down the screen.</summary>
    public const int MaxPreferredListRows = 10;
}
