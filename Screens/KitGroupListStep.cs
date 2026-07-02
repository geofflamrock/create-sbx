using CreateSbx.Models;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>The Kits field's top-level step: lists added kit sources plus a trailing "+ Add kit
/// source" row. Enter opens a source for add/edit, Delete/'d' removes one, Escape closes the
/// popup keeping whatever sources exist (this step manages a list, not a single pending value).</summary>
internal sealed class KitGroupListStep : IStep
{
    private readonly SandboxConfig _config;
    private readonly ListWidget<KitGroupRow> _list;
    private readonly Action<ApplicationContext> _onAdd;
    private readonly Action<ApplicationContext, KitGroup> _onEdit;

    public KitGroupListStep(SandboxConfig config, Action<ApplicationContext> onAdd, Action<ApplicationContext, KitGroup> onEdit)
    {
        _config = config;
        _onAdd = onAdd;
        _onEdit = onEdit;

        var rows = _config.KitGroups.Select(g => new KitGroupRow(g)).Append(new KitGroupRow(null)).ToList();
        _list = new ListWidget<KitGroupRow>(rows)
            .HighlightSymbol("→ ")
            .HighlightStyle(new Style(decoration: Decoration.Bold))
            .WrapAround()
            .SelectedIndex(0);
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

        if (key.Key == Key.Delete || key.Character == 'd')
        {
            if (_list.SelectedItem?.Group is { } group)
            {
                var index = _list.SelectedIndex!.Value;
                _config.KitGroups.Remove(group);
                _list.Items.RemoveAt(index);
                _list.SelectedIndex = Math.Min(index, _list.Items.Count - 1);
            }

            return;
        }

        if (key.Key == Key.Enter)
        {
            var row = _list.SelectedItem;
            if (row?.Group is { } existing)
            {
                _onEdit(context, existing);
            }
            else
            {
                _onAdd(context);
            }

            return;
        }

        _list.KeyMap.HandleKey(key);
    }

    public void Render(RenderContext context)
    {
        var layout = new Layout("Root")
            .SplitRows(new Layout("Label").Size(1), new Layout("List"), new Layout("Footer").Size(1));

        context.Render(Paragraph.FromMarkup("[green]Kit sources[/]:"), layout.GetArea(context, "Label"));
        context.Render(_list, layout.GetArea(context, "List"));
        context.Render(
            Paragraph.FromMarkup("[grey][[Enter]] add/edit  [[Del/d]] remove  [[Esc]] done[/]"),
            layout.GetArea(context, "Footer"));
    }
}
