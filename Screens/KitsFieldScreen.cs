using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Kits field editor: a list of added kit sources, each addable/editable through the
/// shared repo-url + fetch + multi-select flow.</summary>
internal sealed class KitsFieldScreen : MultiStepScreen
{
    private KitGroup? _editingGroup;
    private string? _pendingOwner;
    private string? _pendingRepo;
    private string? _pendingBranch;

    public KitsFieldScreen(SandboxConfig config)
        : base(config)
    {
        Current = BuildGroupListStep();
    }

    private IStep BuildGroupListStep() => new KitGroupListStep(Config, OnAdd, OnEdit);

    private void OnAdd(ApplicationContext context)
    {
        _editingGroup = null;
        Current = new RepoUrlStep(Config, "kit", OnRepoResolved);
    }

    private void OnEdit(ApplicationContext context, KitGroup group)
    {
        _editingGroup = group;
        OnRepoResolved(context, group.Owner, group.Repo, group.Branch);
    }

    private void OnRepoResolved(ApplicationContext context, string owner, string repo, string branch)
    {
        _pendingOwner = owner;
        _pendingRepo = repo;
        _pendingBranch = branch;

        Breadcrumbs.Add($"Repository: {MarkupText.Escape(owner)}/{MarkupText.Escape(repo)}");
        Breadcrumbs.Add($"Branch: {MarkupText.Escape(string.IsNullOrEmpty(branch) ? "(default)" : branch)}");

        Current = new BusyStep($"Fetching {owner}/{repo}...");
        ActiveJob = context.StartJob(async job =>
        {
            try
            {
                // The BusyStep already shows "Fetching {owner}/{repo}..." locally — this status
                // text is about browsing the repo for kits, not the eventual `sbx create`, so it
                // shouldn't also show up in the persistent preview log.
                var cloneDir = await RepoService.EnsureRepoAsync(owner, repo, branch, Config.FetchedRepos, _ => { });
                job.Broadcast(new RepoFetchSucceededMessage(cloneDir));
            }
            catch (Exception ex)
            {
                job.Broadcast(new RepoFetchFailedMessage(ex.Message));
            }
        });
    }

    private void HandleRepoFetched(string cloneDir)
    {
        var kits = RepoService.FindKits(cloneDir);
        if (kits.Count == 0)
        {
            Current = new MessageStep("No kits found in the repository.", _ => Reset());
            return;
        }

        var editing = _editingGroup;
        Current = new MultiSelectEditorScreen<Kit>(
            "Select the kits to include",
            kits,
            FormatKit,
            (context, selected) =>
            {
                if (editing is not null)
                {
                    if (selected.Count == 0)
                    {
                        Config.KitGroups.Remove(editing);
                    }
                    else
                    {
                        editing.SelectedKits = selected;
                    }
                }
                else if (selected.Count > 0)
                {
                    Config.KitGroups.Add(new KitGroup
                    {
                        Owner = _pendingOwner!,
                        Repo = _pendingRepo!,
                        Branch = _pendingBranch ?? "",
                        SelectedKits = selected,
                    });
                }

                Reset();
            },
            isInitiallyChecked: kit => editing?.SelectedKits.Any(k => k.Directory == kit.Directory) ?? false);
    }

    private void Reset()
    {
        Breadcrumbs.Clear();
        Current = BuildGroupListStep();
    }

    private static string FormatKit(Kit kit) => kit.Description is not null
        ? $"{MarkupText.Escape(kit.DisplayName)} [grey]- {MarkupText.Escape(kit.Description)}[/]"
        : MarkupText.Escape(kit.DisplayName);

    protected override bool HandleMessage(ApplicationContext context, ApplicationMessage message)
    {
        if (Current is not BusyStep)
        {
            return false;
        }

        switch (message)
        {
            case RepoFetchSucceededMessage success:
                HandleRepoFetched(success.CloneDir);
                return true;
            case RepoFetchFailedMessage failure:
                Current = new MessageStep($"Failed to fetch repository: {failure.Error}", _ => Reset());
                return true;
            default:
                return false;
        }
    }
}
