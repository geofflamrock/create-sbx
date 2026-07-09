using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>A single-line text input step, rendered inline (label, then the input on the same
/// row — no box). Used standalone (wrapped in <see cref="SimpleFieldScreen"/>) for simple fields
/// like Name/Working directory, and embedded as one step of a composite field screen (e.g.
/// entering a custom agent id, an image name, a branch). Escape always pops back to the previous
/// screen; Enter validates (if a validator was supplied) and, once valid, hands control back to
/// the caller via <paramref name="onConfirm"/> — which decides whether that means popping back or
/// advancing to another step.</summary>
internal sealed class TextFieldEditorScreen : IStep
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

    public void Render(RenderContext context)
    {
        var layout = new Layout("Root")
            .SplitRows(new Layout("Row").Size(1), new Layout("Footer"));

        var rowArea = layout.GetArea(context, "Row");
        var labelText = $"{_label}: ";
        var row = new Layout("Row")
            .SplitColumns(new Layout("Label").Size(labelText.Length), new Layout("Input"));

        context.Render(Paragraph.FromMarkup($"[green]{MarkupText.Escape(labelText)}[/]"), row.GetArea(rowArea, "Label"));
        context.Render(_textBox, row.GetArea(rowArea, "Input"));

        var footer = _error is not null
            ? $"[red]{MarkupText.Escape(_error)}[/]"
            : _hint is not null ? $"[grey]{MarkupText.Escape(_hint)}[/]" : null;

        if (footer is not null)
        {
            context.Render(Paragraph.FromMarkup(footer), layout.GetArea(context, "Footer"));
        }
    }

    public IEnumerable<KeyBinding> Help()
    {
        yield return KeyBinding.For(Key.Escape).WithHelp("Back");
        yield return KeyBinding.For(Key.Enter).WithHelp("Confirm");
    }
}
