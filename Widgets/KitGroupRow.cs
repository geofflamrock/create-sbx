using CreateSbx.Models;

namespace CreateSbx.Widgets;

/// <summary>A row in the Kits field's group list: either an existing kit source, or (when
/// <see cref="Group"/> is <see langword="null"/>) the trailing "+ Add kit source" action.</summary>
internal sealed class KitGroupRow(KitGroup? group) : IListWidgetItem
{
    public KitGroup? Group { get; } = group;

    public Text CreateText(bool isSelected)
    {
        if (Group is null)
        {
            return Text.FromMarkup("[green]+ Add kit source[/]");
        }

        var branchLabel = string.IsNullOrEmpty(Group.Branch) ? "" : $" ({MarkupText.Escape(Group.Branch)})";
        var count = Group.SelectedKits.Count;
        return Text.FromMarkup(
            $"{MarkupText.Escape(Group.Owner)}/{MarkupText.Escape(Group.Repo)}{branchLabel} — {count} kit{(count == 1 ? "" : "s")}");
    }
}
