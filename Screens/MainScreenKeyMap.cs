namespace CreateSbx.Screens;

internal sealed class MainScreenKeyMap : IKeyMap
{
    public KeyBinding Select { get; set; } = KeyBinding.For(Key.Enter).WithHelp("Select");
    public KeyBinding Quit { get; set; } = KeyBinding.For('q').WithHelp("Quit");

    public IEnumerable<KeyBinding> Help()
    {
        yield return Select;
        yield return Quit;
    }
}
