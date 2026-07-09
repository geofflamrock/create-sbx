namespace CreateSbx.Screens;

/// <summary>Wraps a single-step field editor (Name, Working directory, Workspace mode, ...) so
/// it gets the same left-aligned help bar as the composite field screens, without needing
/// breadcrumbs or a background job of its own.</summary>
internal sealed class SimpleFieldScreen : MultiStepScreen
{
    public SimpleFieldScreen(IStep step)
    {
        Current = step;
    }
}
