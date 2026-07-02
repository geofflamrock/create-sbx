namespace CreateSbx.Widgets;

internal enum FieldId { Name, Agent, WorkDir, WorkspaceMode, Template, Kits, Create, Exit }

/// <summary>A row in the main field list: either a data field ("Label: value") or an action row
/// ("Create sandbox" / "Exit", rendered without a value).</summary>
internal sealed class FieldListItem(FieldId id, string label, Func<string>? getValue) : IListWidgetItem
{
    public FieldId Id { get; } = id;

    public Text CreateText(bool isSelected)
    {
        if (getValue is null)
        {
            var style = Id == FieldId.Exit ? "red" : "green";
            return Text.FromMarkup($"[{style} bold]{MarkupText.Escape(label)}[/]");
        }

        return Text.FromMarkup($"[green]{MarkupText.Escape(label)}[/]: {MarkupText.Escape(getValue())}");
    }
}
