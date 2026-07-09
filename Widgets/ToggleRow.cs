namespace CreateSbx.Widgets;

/// <summary>A checklist row: a value plus whether it's currently checked. Ports the
/// <c>ToDoItem</c>/<c>IListWidgetItem</c> toggle pattern from the spectre.tui sample's
/// <c>TodoWidget</c> into a generic, reusable form.</summary>
internal sealed class ToggleRow<T>(T value, Func<T, string> format) : IListWidgetItem
{
    public T Value { get; } = value;
    public bool Checked { get; set; }

    public void Toggle() => Checked = !Checked;

    public Text CreateText(bool isSelected)
    {
        var checkbox = Checked ? "[green]☑[/]" : "☐";
        return Text.FromMarkup($"{checkbox} {format(Value)}");
    }
}
