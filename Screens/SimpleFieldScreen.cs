using CreateSbx.Models;

namespace CreateSbx.Screens;

/// <summary>Wraps a single-step field editor (Name, Workspace directory, Workspace mode, ...) so
/// it gets the same shell (title, preview, help bar) as the composite field screens, without
/// needing breadcrumbs or a background job of its own.</summary>
internal sealed class SimpleFieldScreen : MultiStepScreen
{
    public SimpleFieldScreen(SandboxConfig config, IStep step)
        : base(config)
    {
        Current = step;
    }
}
