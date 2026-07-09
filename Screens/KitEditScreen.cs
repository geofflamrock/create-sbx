using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Adds a new kit source, or re-opens an existing one to change which of its kits are
/// included. The main screen's field list owns the list of sources itself (add/edit/delete each
/// show up as their own row there) — this screen only handles the repo-url/fetch/multi-select
/// sub-flow for a single source.</summary>
internal sealed class KitEditScreen : MultiStepScreen
{
    private readonly KitGroup? _editingGroup;
    private string? _pendingOwner;
    private string? _pendingRepo;
    private string? _pendingBranch;

    public KitEditScreen(SandboxConfig config, KitGroup? editingGroup)
        : base(config)
    {
        _editingGroup = editingGroup;

        if (editingGroup is null)
        {
            Current = new RepoUrlStep(Config, "kit", OnRepoResolved, AddBreadcrumb);
        }
        else
        {
            AddBreadcrumb("Repository", $"{editingGroup.Owner}/{editingGroup.Repo}");
            AddBreadcrumb("Branch", string.IsNullOrEmpty(editingGroup.Branch) ? "(default)" : editingGroup.Branch);
            Current = new BusyStep($"Fetching {editingGroup.Owner}/{editingGroup.Repo}...");
        }
    }

    public override void OnEnter(ApplicationContext context)
    {
        if (_editingGroup is { } group)
        {
            OnRepoResolved(context, group.Owner, group.Repo, group.Branch);
        }
    }

    private void AddBreadcrumb(string label, string value) => Breadcrumbs.Add($"{label}: {MarkupText.Escape(value)}");

    private void OnRepoResolved(ApplicationContext context, string owner, string repo, string branch)
    {
        _pendingOwner = owner;
        _pendingRepo = repo;
        _pendingBranch = branch;

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
            Current = new MessageStep("No kits found in the repository.", ctx => ctx.Pop());
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

                context.Pop();
            },
            isInitiallyChecked: kit => editing?.SelectedKits.Any(k => k.Directory == kit.Directory) ?? false);
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
                Current = new MessageStep($"Failed to fetch repository: {failure.Error}", ctx => ctx.Pop());
                return true;
            default:
                return false;
        }
    }
}
