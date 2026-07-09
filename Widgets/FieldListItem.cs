namespace CreateSbx.Widgets;

internal enum FieldId { Name, Agent, WorkDir, WorkspaceMode, Template, Kits, Spacer, Create, Exit }

/// <summary>A row in the main field list: a data field (label/value in two aligned columns), a
/// blank spacer row (skipped during navigation), or an action row ("Create sandbox" / "Exit",
/// rendered without a value).</summary>
internal sealed class FieldListItem(FieldId id, string label, Func<string>? getValue, int labelColumnWidth = 0) : IListWidgetItem
{
    public FieldId Id { get; } = id;

    public Text CreateText(bool isSelected)
    {
        if (Id == FieldId.Spacer)
        {
            return Text.FromString("");
        }

        if (getValue is null)
        {
            var style = Id == FieldId.Exit ? "red" : "green";
            return Text.FromMarkup($"[{style} bold]{MarkupText.Escape(label)}[/]");
        }

        var paddedLabel = MarkupText.Escape(label).PadRight(labelColumnWidth);
        return Text.FromMarkup($"[green]{paddedLabel}[/]  {MarkupText.Escape(getValue())}");
    }
}
