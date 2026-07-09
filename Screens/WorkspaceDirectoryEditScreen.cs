using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Adds a new additional workspace directory, or re-opens an existing one to change its
/// mount mode. The main screen's field list owns the list of directories itself (add/edit/delete
/// each show up as their own row there) — this screen only handles picking a path (for a new
/// entry) and choosing read/write vs. read-only.</summary>
internal sealed class WorkspaceDirectoryEditScreen : MultiStepScreen
{
    private const string EnterPathSentinel = "Enter path";

    private readonly WorkspaceDirectory? _editing;

    public WorkspaceDirectoryEditScreen(SandboxConfig config, WorkspaceDirectory? editing)
        : base(config)
    {
        _editing = editing;

        if (editing is null)
        {
            Current = BuildPathStep();
        }
        else
        {
            Breadcrumbs.Add($"Path: {MarkupText.Escape(editing.Path)}");
            Current = BuildModeStep(editing.Path, editing.ReadOnly);
        }
    }

    private IStep BuildPathStep()
    {
        if (Config.RecentWorkspaceDirectories.Count == 0)
        {
            return BuildPathTextStep(string.Empty);
        }

        var options = new List<string>(Config.RecentWorkspaceDirectories) { EnterPathSentinel };
        return new SingleSelectEditorScreen<string>(
            "Select an additional workspace directory",
            options,
            path => path == EnterPathSentinel ? "[grey]Enter path…[/]" : MarkupText.Escape(path),
            (context, chosen) =>
            {
                if (chosen == EnterPathSentinel)
                {
                    Current = BuildPathTextStep(string.Empty);
                    return;
                }

                OnPathChosen(chosen);
            });
    }

    private IStep BuildPathTextStep(string initialValue) =>
        new TextFieldEditorScreen(
            "Enter the path to the additional workspace directory",
            initialValue,
            (context, path) => OnPathChosen(path.Trim()),
            placeholder: "/path/to/directory",
            validate: path => string.IsNullOrWhiteSpace(path) ? "A path is required." : null);

    private void OnPathChosen(string path)
    {
        RecentEntriesStore.Add(Config.RecentWorkspaceDirectories, path, RecentEntriesStore.WorkspaceDirectoriesFile);
        Breadcrumbs.Add($"Path: {MarkupText.Escape(path)}");
        Current = BuildModeStep(path, readOnly: false);
    }

    private IStep BuildModeStep(string path, bool readOnly) =>
        new SingleSelectEditorScreen<bool>(
            $"Mount {MarkupText.Escape(path)} as",
            [false, true],
            mode => mode ? "Read-only" : "Read/write",
            (context, selectedReadOnly) =>
            {
                if (_editing is not null)
                {
                    _editing.ReadOnly = selectedReadOnly;
                }
                else
                {
                    Config.AdditionalWorkspaceDirectories.Add(new WorkspaceDirectory { Path = path, ReadOnly = selectedReadOnly });
                }

                context.Pop();
            },
            readOnly ? 1 : 0);
}
