namespace CreateSbx.Screens;

internal sealed class MainScreenKeyMap : IKeyMap
{
    public KeyBinding MoveUp { get; set; } = KeyBinding.For(Key.Up);

    public KeyBinding MoveDown { get; set; } = KeyBinding.For(Key.Down);

    public KeyBinding Select { get; set; } = KeyBinding.For(Key.Enter).WithHelp("Select");

    public KeyBinding RemoveKit { get; set; } =
        KeyBinding.Combine(KeyBinding.For(Key.Delete), KeyBinding.For('d')).WithHelp("Remove kit");

    public KeyBinding Quit { get; set; } = KeyBinding.For(KeyPress.For('c').WithCtrl()).WithHelp("Exit");

    public IEnumerable<KeyBinding> Help()
    {
        yield return KeyBinding.Combine(MoveUp, MoveDown).WithHelp("Move");
        yield return Select;
        yield return RemoveKit;
        yield return Quit;
    }
}
