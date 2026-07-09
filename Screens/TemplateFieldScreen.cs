using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Template field editor: None / a registry image / a Dockerfile from a git repository
/// / a local Dockerfile. The git-repository path fetches the repo as a background job and lets
/// the user pick one of the discovered Dockerfiles.</summary>
internal sealed class TemplateFieldScreen : MultiStepScreen
{
    private readonly Action<ApplicationContext, TemplateConfig?> _onConfirm;
    private string? _pendingOwner;
    private string? _pendingRepo;
    private string? _pendingBranch;

    public TemplateFieldScreen(SandboxConfig config, Action<ApplicationContext, TemplateConfig?> onConfirm)
        : base(config)
    {
        _onConfirm = onConfirm;
        Current = BuildSourceSelectStep();
    }

    private IStep BuildSourceSelectStep()
    {
        var options = new TemplateSource?[] { null, TemplateSource.Registry, TemplateSource.GitRepo, TemplateSource.Local };
        return new SingleSelectEditorScreen<TemplateSource?>(
            "Select template source",
            options,
            FormatSource,
            (context, source) =>
            {
                switch (source)
                {
                    case null:
                        _onConfirm(context, null);
                        break;
                    case TemplateSource.Registry:
                        Breadcrumbs.Add($"Template source: {FormatSource(source)}");
                        Current = BuildImageNameStep();
                        break;
                    case TemplateSource.GitRepo:
                        Breadcrumbs.Add($"Template source: {FormatSource(source)}");
                        Current = new RepoUrlStep(Config, "template", OnGitRepoResolved, AddBreadcrumb);
                        break;
                    case TemplateSource.Local:
                        Breadcrumbs.Add($"Template source: {FormatSource(source)}");
                        Current = BuildLocalPathStep();
                        break;
                }
            });
    }

    private void AddBreadcrumb(string label, string value) => Breadcrumbs.Add($"{label}: {MarkupText.Escape(value)}");

    private static string FormatSource(TemplateSource? source) => source switch
    {
        null => "None [grey](use the default sandbox template)[/]",
        TemplateSource.Registry => "Docker image",
        TemplateSource.GitRepo => "Dockerfile - Git repository",
        TemplateSource.Local => "Dockerfile - local",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private IStep BuildImageNameStep() =>
        new TextFieldEditorScreen(
            "Enter the image name",
            "",
            (context, imageName) => _onConfirm(context, new TemplateConfig(TemplateSource.Registry, imageName.Trim(), null, null)),
            placeholder: "e.g. ubuntu:22.04",
            validate: name => string.IsNullOrWhiteSpace(name) ? "Image name is required." : null);

    private IStep BuildLocalPathStep() =>
        new TextFieldEditorScreen(
            "Enter the path to the Dockerfile",
            "",
            (context, path) =>
            {
                var fullPath = Path.GetFullPath(path.Trim());
                var dockerContext = Path.GetDirectoryName(fullPath)!;
                var config = new TemplateConfig(TemplateSource.Local, GenerateImageName(), fullPath, dockerContext);
                _onConfirm(context, config);
            },
            placeholder: "/path/to/Dockerfile",
            validate: path => File.Exists(Path.GetFullPath(path.Trim()))
                ? null
                : $"Dockerfile not found: {Path.GetFullPath(path.Trim())}");

    private void OnGitRepoResolved(ApplicationContext context, string owner, string repo, string branch)
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
                // text is about browsing the repo for Dockerfiles, not the eventual `sbx create`,
                // so it shouldn't also show up in the persistent preview log.
                var cloneDir = await RepoService.EnsureRepoAsync(owner, repo, branch, Config.FetchedRepos, _ => { });
                job.Broadcast(new RepoFetchSucceededMessage(cloneDir));
            }
            catch (Exception ex)
            {
                job.Broadcast(new RepoFetchFailedMessage(ex.Message));
            }
        });
    }

    private void HandleGitRepoFetched(string cloneDir)
    {
        var dockerfiles = RepoService.FindDockerfiles(cloneDir);
        if (dockerfiles.Count == 0)
        {
            Current = new MessageStep("No Dockerfiles found in the repository.", _ => Reset());
            return;
        }

        Current = new SingleSelectEditorScreen<string>(
            "Select a Dockerfile",
            dockerfiles,
            MarkupText.Escape,
            (context, dockerfile) =>
            {
                var absolutePath = Path.Combine(cloneDir, dockerfile);
                var config = new TemplateConfig(
                    TemplateSource.GitRepo,
                    GenerateImageName(),
                    absolutePath,
                    cloneDir,
                    _pendingBranch,
                    $"{_pendingOwner}/{_pendingRepo}");
                _onConfirm(context, config);
            });
    }

    private void Reset()
    {
        Breadcrumbs.Clear();
        Current = BuildSourceSelectStep();
    }

    private static string GenerateImageName() => $"create-sbx-{Guid.NewGuid().ToString("N")[..8]}";

    protected override bool HandleMessage(ApplicationContext context, ApplicationMessage message)
    {
        if (Current is not BusyStep)
        {
            return false;
        }

        switch (message)
        {
            case RepoFetchSucceededMessage success:
                HandleGitRepoFetched(success.CloneDir);
                return true;
            case RepoFetchFailedMessage failure:
                Current = new MessageStep($"Failed to fetch repository: {failure.Error}", _ => Reset());
                return true;
            default:
                return false;
        }
    }
}
