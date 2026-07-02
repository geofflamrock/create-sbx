using System.CommandLine;
using System.Text;
using CreateSbx.Screens;

Console.OutputEncoding = Encoding.UTF8;
Console.Title = "create-sbx";

var rootCommand = new RootCommand("An interactive TUI for creating Docker Sandboxes using `sbx`");
rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) => await RunAsync());
return await rootCommand.Parse(args).InvokeAsync();

async Task<int> RunAsync()
{
    var settings = new ApplicationSettings
    {
        Terminal = Terminal.Create(new InlineMode(24)),
    };

    var mainScreen = new MainScreen();
    await Application.Create(settings).RunAsync(mainScreen);
    return mainScreen.ExitCode ?? 0;
}
