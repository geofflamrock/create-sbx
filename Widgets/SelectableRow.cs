namespace CreateSbx.Widgets;

/// <summary>Adapts any value to <see cref="IListWidgetItem"/> for use in a <c>ListWidget&lt;T&gt;</c>,
/// so editor screens don't need their item models to know how to render themselves.</summary>
internal sealed class SelectableRow<T>(T value, Func<T, string> format) : IListWidgetItem
{
    public T Value { get; } = value;

    public Text CreateText(bool isSelected) => Text.FromMarkup(format(Value));
}
