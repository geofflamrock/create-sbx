using CreateSbx.Models;

namespace CreateSbx.Widgets;

internal enum FieldId { Name, Agent, WorkDir, WorkspaceMode, Template, KitGroup, AddKit, Spacer, Create, Exit }

/// <summary>A row in the main field list: a data field (label/value in two aligned columns), a
/// kit source row or the trailing "+ Add kit source" row (both part of the Kits "section", so
/// only the first row in that section shows the "Kits" label — the rest indent to line up under
/// the value column), a blank spacer row (skipped during navigation), or an action row ("Create
/// sandbox" / "Exit", rendered without a value). White when idle, green — label and value alike —
/// when selected.</summary>
internal sealed class FieldListItem : IListWidgetItem
{
    private readonly string? _label;
    private readonly Func<string>? _getValue;
    private readonly int _labelColumnWidth;

    public FieldId Id { get; }

    public KitGroup? Group { get; }

    private FieldListItem(FieldId id, string? label, Func<string>? getValue, int labelColumnWidth, KitGroup? group)
    {
        Id = id;
        _label = label;
        _getValue = getValue;
        _labelColumnWidth = labelColumnWidth;
        Group = group;
    }

    public static FieldListItem Field(FieldId id, string label, Func<string> getValue, int labelColumnWidth) =>
        new(id, label, getValue, labelColumnWidth, null);

    public static FieldListItem Action(FieldId id, string label) => new(id, label, null, 0, null);

    public static FieldListItem Spacer() => new(FieldId.Spacer, null, null, 0, null);

    public static FieldListItem ForKitGroup(KitGroup group, bool showLabel, int labelColumnWidth) =>
        new(FieldId.KitGroup, showLabel ? "Kits" : null, () => FormatKitGroup(group), labelColumnWidth, group);

    public static FieldListItem AddKitRow(bool showLabel, int labelColumnWidth) =>
        new(FieldId.AddKit, showLabel ? "Kits" : null, () => "+ Add kit source", labelColumnWidth, null);

    private static string FormatKitGroup(KitGroup group)
    {
        var branchSuffix = string.IsNullOrEmpty(group.Branch) ? "" : $" ({group.Branch})";
        var kitNames = string.Join(", ", group.SelectedKits.Select(k => k.DisplayName));
        return $"{group.Owner}/{group.Repo}{branchSuffix} — {kitNames}";
    }

    public Text CreateText(bool isSelected)
    {
        if (Id == FieldId.Spacer)
        {
            return Text.FromString("");
        }

        var color = isSelected ? "green" : "white";

        if (_getValue is null)
        {
            return Text.FromMarkup($"[{color} bold]{MarkupText.Escape(_label!)}[/]");
        }

        var escapedValue = MarkupText.Escape(_getValue());

        if (_label is null)
        {
            var indent = new string(' ', _labelColumnWidth + 2);
            return Text.FromMarkup($"{indent}[{color}]{escapedValue}[/]");
        }

        var paddedLabel = MarkupText.Escape(_label).PadRight(_labelColumnWidth);
        return Text.FromMarkup($"[{color}]{paddedLabel}[/]  [{color}]{escapedValue}[/]");
    }
}
