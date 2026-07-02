using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>A checklist step. Space toggles the highlighted row, Escape pops the whole popup
/// (discarding the selection), Enter hands the checked items back via
/// <paramref name="onConfirm"/>.</summary>
internal sealed class MultiSelectEditorScreen<T> : Screen, IStep
{
    private readonly string _label;
    private readonly ListWidget<ToggleRow<T>> _list;
    private readonly Action<ApplicationContext, List<T>> _onConfirm;

    public MultiSelectEditorScreen(
        string label,
        IEnumerable<T> items,
        Func<T, string> format,
        Action<ApplicationContext, List<T>> onConfirm,
        Func<T, bool>? isInitiallyChecked = null)
    {
        _label = label;
        _onConfirm = onConfirm;

        var rows = items
            .Select(item => new ToggleRow<T>(item, format) { Checked = isInitiallyChecked?.Invoke(item) ?? false })
            .ToList();

        _list = new ListWidget<ToggleRow<T>>(rows)
            .HighlightSymbol("→ ")
            .HighlightStyle(new Style(decoration: Decoration.Bold))
            .WrapAround()
            .SelectedIndex(rows.Count == 0 ? null : 0);
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

        if (key.Key == Key.Space)
        {
            _list.SelectedItem?.Toggle();
            return;
        }

        if (key.Key == Key.Enter)
        {
            var selected = _list.Items.Where(row => row.Checked).Select(row => row.Value).ToList();
            _onConfirm(context, selected);
            return;
        }

        _list.KeyMap.HandleKey(key);
    }

    public override void Render(RenderContext context)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Label").Size(1),
                new Layout("List"),
                new Layout("Footer").Size(1));

        context.Render(Paragraph.FromMarkup($"[green]{MarkupText.Escape(_label)}[/]:"), layout.GetArea(context, "Label"));
        context.Render(_list, layout.GetArea(context, "List"));
        context.Render(
            Paragraph.FromMarkup("[grey][[Space]] toggle  [[Enter]] confirm[/]"),
            layout.GetArea(context, "Footer"));
    }
}
