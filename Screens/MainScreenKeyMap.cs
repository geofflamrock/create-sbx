namespace CreateSbx.Screens;

internal sealed class MainScreenKeyMap : IKeyMap
{
    public KeyBinding MoveUp { get; set; } = KeyBinding.For(Key.Up);

    public KeyBinding MoveDown { get; set; } = KeyBinding.For(Key.Down);

    public KeyBinding Select { get; set; } = KeyBinding.For(Key.Enter).WithHelp("Select");

    public KeyBinding Remove { get; set; } = KeyBinding.Combine(KeyBinding.For(Key.Delete), KeyBinding.For('d'));

    public KeyBinding Quit { get; set; } =
        KeyBinding.Combine(KeyBinding.For(KeyPress.For('c').WithCtrl()), KeyBinding.For(Key.Escape)).WithHelp("Exit");

    /// <summary>Set by <see cref="MainScreen"/> each render to reflect what's currently
    /// selected — the remove shortcut only makes sense (and only shows up in the help bar) when
    /// a kit source or additional workspace directory row is highlighted.</summary>
    public string? RemoveHelpText { get; set; }

    public IEnumerable<KeyBinding> Help()
    {
        yield return KeyBinding.Combine(MoveUp, MoveDown).WithHelp("Move");
        yield return Select;

        if (RemoveHelpText is not null)
        {
            yield return Remove.WithHelp(RemoveHelpText);
        }

        yield return Quit;
    }
}
