using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>A single-select list step. Escape pops the whole popup; Enter hands the highlighted
/// item back via <paramref name="onSelect"/>, which decides whether that closes the popup or
/// advances to another step.</summary>
internal sealed class SingleSelectEditorScreen<T> : Screen, IStep
{
    private readonly string _label;
    private readonly ListWidget<SelectableRow<T>> _list;
    private readonly Action<ApplicationContext, T> _onSelect;

    public SingleSelectEditorScreen(
        string label,
        IEnumerable<T> items,
        Func<T, string> format,
        Action<ApplicationContext, T> onSelect,
        int selectedIndex = 0)
    {
        _label = label;
        _onSelect = onSelect;

        var rows = items.Select(item => new SelectableRow<T>(item, format)).ToList();
        _list = new ListWidget<SelectableRow<T>>(rows)
            .HighlightSymbol("→ ")
            .HighlightStyle(new Style(decoration: Decoration.Bold))
            .WrapAround()
            .SelectedIndex(rows.Count == 0 ? null : Math.Clamp(selectedIndex, 0, rows.Count - 1));
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
            if (_list.SelectedItem is { } row)
            {
                _onSelect(context, row.Value);
            }

            return;
        }

        _list.KeyMap.HandleKey(key);
    }

    public override void Render(RenderContext context)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Label").Size(1),
                new Layout("List"));

        context.Render(Paragraph.FromMarkup($"[green]{MarkupText.Escape(_label)}[/]:"), layout.GetArea(context, "Label"));
        context.Render(_list, layout.GetArea(context, "List"));
    }
}
