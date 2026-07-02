using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Kits field editor: a list of added kit sources, each addable/editable through the
/// shared repo-url + fetch + multi-select flow.</summary>
internal sealed class KitsFieldScreen : Screen
{
    private readonly SandboxConfig _config;
    private IStep _current;
    private IJobHandle? _activeJob;
    private KitGroup? _editingGroup;
    private string? _pendingOwner;
    private string? _pendingRepo;
    private string? _pendingBranch;

    public KitsFieldScreen(SandboxConfig config)
    {
        _config = config;
        _current = BuildGroupListStep();
    }

    private IStep BuildGroupListStep() => new KitGroupListStep(_config, OnAdd, OnEdit);

    private void OnAdd(ApplicationContext context)
    {
        _editingGroup = null;
        _current = new RepoUrlStep(_config, "kit", OnRepoResolved);
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
        _current = new BusyStep($"Fetching {owner}/{repo}...");
        _activeJob = context.StartJob(async job =>
        {
            try
            {
                var cloneDir = await RepoService.EnsureRepoAsync(
                    owner, repo, branch, _config.FetchedRepos, status => job.Broadcast(new LogMessage(status)));
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
            _current = new MessageStep("No kits found in the repository.", _ => _current = BuildGroupListStep());
            return;
        }

        var editing = _editingGroup;
        _current = new MultiSelectEditorScreen<Kit>(
            "Select the kits to include",
            kits,
            FormatKit,
            (context, selected) =>
            {
                if (editing is not null)
                {
                    if (selected.Count == 0)
                    {
                        _config.KitGroups.Remove(editing);
                    }
                    else
                    {
                        editing.SelectedKits = selected;
                    }
                }
                else if (selected.Count > 0)
                {
                    _config.KitGroups.Add(new KitGroup
                    {
                        Owner = _pendingOwner!,
                        Repo = _pendingRepo!,
                        Branch = _pendingBranch ?? "",
                        SelectedKits = selected,
                    });
                }

                _current = BuildGroupListStep();
            },
            isInitiallyChecked: kit => editing?.SelectedKits.Any(k => k.Directory == kit.Directory) ?? false);
    }

    private static string FormatKit(Kit kit) => kit.Description is not null
        ? $"{MarkupText.Escape(kit.DisplayName)} [grey]- {MarkupText.Escape(kit.Description)}[/]"
        : MarkupText.Escape(kit.DisplayName);

    public override void OnLeave(ApplicationContext context) => _activeJob?.Cancel();

    public override void OnMessage(ApplicationContext context, ApplicationMessage message)
    {
        if (_current is BusyStep)
        {
            switch (message)
            {
                case RepoFetchSucceededMessage success:
                    HandleRepoFetched(success.CloneDir);
                    return;
                case RepoFetchFailedMessage failure:
                    _current = new MessageStep($"Failed to fetch repository: {failure.Error}", _ => _current = BuildGroupListStep());
                    return;
            }
        }

        _current.OnMessage(context, message);
    }

    public override void Update(FrameInfo frame, IRenderBounds bounds)
    {
        if (_current is BusyStep busy)
        {
            busy.Update(frame);
        }
    }

    public override void Render(RenderContext context) => _current.Render(context);
}
