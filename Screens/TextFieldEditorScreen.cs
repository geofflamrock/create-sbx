using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>A single-line text input step. Used standalone (pushed directly inside a
/// <see cref="FieldPopup"/>) for simple fields like Name/Working directory, and embedded as one
/// step of a composite field screen (e.g. entering a custom agent id, an image name, a branch).
/// Escape always pops the whole popup; Enter validates (if a validator was supplied) and, once
/// valid, hands control back to the caller via <paramref name="onConfirm"/> — which decides
/// whether that means closing the popup or advancing to another step.</summary>
internal sealed class TextFieldEditorScreen : Screen, IStep
{
    private readonly TextBoxWidget _textBox;
    private readonly string _label;
    private readonly string? _hint;
    private readonly Func<string, string?>? _validate;
    private readonly Action<ApplicationContext, string> _onConfirm;
    private string? _error;

    public TextFieldEditorScreen(
        string label,
        string initialValue,
        Action<ApplicationContext, string> onConfirm,
        string? placeholder = null,
        string? hint = null,
        Func<string, string?>? validate = null)
    {
        _label = label;
        _hint = hint;
        _validate = validate;
        _onConfirm = onConfirm;
        _textBox = new TextBoxWidget().AsSingleLine().Text(initialValue).Placeholder(placeholder ?? "");
        _textBox.IsFocused = true;
    }

    public override void OnMessage(ApplicationContext context, ApplicationMessage message)
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

        if (key.Key == Key.Enter)
        {
            var value = _textBox.Text;
            var error = _validate?.Invoke(value);
            if (error is not null)
            {
                _error = error;
                return;
            }

            _error = null;
            _onConfirm(context, value);
            return;
        }

        _textBox.KeyMap.HandleKey(key);
    }

    public override void Render(RenderContext context)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Label").Size(1),
                new Layout("Input").Size(3),
                new Layout("Footer"));

        context.Render(Paragraph.FromMarkup($"[green]{MarkupText.Escape(_label)}[/]:"), layout.GetArea(context, "Label"));
        context.Render(new BoxWidget().Border(Border.Rounded).Inner(_textBox), layout.GetArea(context, "Input"));

        var footer = _error is not null
            ? $"[red]{MarkupText.Escape(_error)}[/]"
            : _hint is not null ? $"[grey]{MarkupText.Escape(_hint)}[/]" : null;

        if (footer is not null)
        {
            context.Render(Paragraph.FromMarkup(footer), layout.GetArea(context, "Footer"));
        }
    }
}
