using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Shared shape for every field screen: an optional trail of answered breadcrumbs, the
/// current step's content, and a left-aligned help bar describing the current step's keys —
/// stacked top to bottom, all left-aligned. <see cref="SimpleFieldScreen"/> uses this directly
/// for single-step fields; composite fields (Template, Kits, Agent) subclass it to swap
/// <see cref="Current"/> between steps and grow <see cref="Breadcrumbs"/> as the user answers.</summary>
internal abstract class MultiStepScreen : Screen
{
    protected List<string> Breadcrumbs { get; } = [];

    /// <summary>Always set by the constructor of whichever concrete screen this is — a subclass
    /// either passes it to <see cref="SimpleFieldScreen"/> or assigns it in its own constructor
    /// body before anything can render.</summary>
    protected IStep Current { get; set; } = null!;

    protected IJobHandle? ActiveJob { get; set; }

    public override void OnLeave(ApplicationContext context)
    {
        // The job's CancellationTokenSource is disposed once its work delegate finishes, so
        // cancelling a job that already completed (e.g. the user backed out after a fetch
        // succeeded) would throw ObjectDisposedException — only cancel if it's still running.
        if (ActiveJob is { Completion.IsCompleted: false })
        {
            ActiveJob.Cancel();
        }
    }

    public override void OnMessage(ApplicationContext context, ApplicationMessage message)
    {
        if (!HandleMessage(context, message))
        {
            Current.OnMessage(context, message);
        }
    }

    /// <summary>Lets subclasses intercept a message (e.g. a job's fetch-result broadcast) before
    /// it reaches <see cref="Current"/>. Return true to consume it.</summary>
    protected virtual bool HandleMessage(ApplicationContext context, ApplicationMessage message) => false;

    public override void Update(FrameInfo frame, IRenderBounds bounds)
    {
        if (Current is BusyStep busy)
        {
            busy.Update(frame);
        }
    }

    public override void Render(RenderContext context)
    {
        Layout layout;
        if (Breadcrumbs.Count > 0)
        {
            layout = new Layout("Root")
                .SplitRows(
                    new Layout("Breadcrumbs").Size(Breadcrumbs.Count),
                    new Layout("Content"),
                    new Layout("Help").Size(1));

            // Breadcrumbs are pre-composed markup (dynamic parts already escaped by whoever
            // added them) rather than raw text, since some intentionally embed markup like
            // [grey](default)[/] — escaping the whole line here would double-escape those tags.
            var text = string.Join("\n", Breadcrumbs.Select(b => $"[grey]{b}[/]"));
            context.Render(Paragraph.FromMarkup(text), layout.GetArea(context, "Breadcrumbs"));
        }
        else
        {
            layout = new Layout("Root")
                .SplitRows(new Layout("Content"), new Layout("Help").Size(1));
        }

        context.Render(Current, layout.GetArea(context, "Content"));
        context.Render(new HelpWidget(Current).LeftAligned(), layout.GetArea(context, "Help"));
    }
}
