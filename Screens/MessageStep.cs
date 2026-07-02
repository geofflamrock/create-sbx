using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Shows a message (an error, or "no results found") until the user presses any key.</summary>
internal sealed class MessageStep : IStep
{
    private readonly string _message;
    private readonly Action<ApplicationContext> _onContinue;

    public MessageStep(string message, Action<ApplicationContext> onContinue)
    {
        _message = message;
        _onContinue = onContinue;
    }

    public void OnMessage(ApplicationContext context, ApplicationMessage message)
    {
        if (message is not KeyMessage key)
        {
            return;
        }

        if (key.Key == Key.Escape)
        {
            context.Pop();
            return;
        }

        _onContinue(context);
    }

    public void Render(RenderContext context)
    {
        context.Render(
            Paragraph.FromMarkup($"[yellow]{MarkupText.Escape(_message)}[/]\n\n[grey]Press any key to go back[/]")
                .Centered());
    }
}
