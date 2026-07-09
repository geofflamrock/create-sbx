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
            Paragraph.FromMarkup($"[yellow]{MarkupText.Escape(_message)}[/]\n\n[grey]Press any key to go back[/]"));
    }

    public IEnumerable<KeyBinding> Help()
    {
        yield return KeyBinding.For(Key.Escape).WithHelp("Back");
    }

    // Message plus a blank line plus the "press any key" prompt; message itself may still wrap
    // onto more lines than this at render time, in which case it's clipped rather than pushing
    // the help bar further down the screen.
    public int PreferredHeight => _message.Split('\n').Length + 2;
}
