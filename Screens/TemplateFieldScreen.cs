using CreateSbx.Messages;
using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Template field editor: None / a registry image / a Dockerfile from a git repository
/// / a local Dockerfile. The git-repository path fetches the repo as a background job and lets
/// the user pick one of the discovered Dockerfiles.</summary>
internal sealed class TemplateFieldScreen : Screen
{
    private readonly SandboxConfig _config;
    private readonly Action<ApplicationContext, TemplateConfig?> _onConfirm;
    private IStep _current;
    private IJobHandle? _activeJob;
    private string? _pendingBranch;

    public TemplateFieldScreen(SandboxConfig config, Action<ApplicationContext, TemplateConfig?> onConfirm)
    {
        _config = config;
        _onConfirm = onConfirm;
        _current = BuildSourceSelectStep();
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
                        _current = BuildImageNameStep();
                        break;
                    case TemplateSource.GitRepo:
                        _current = new RepoUrlStep(_config, "template", OnGitRepoResolved);
                        break;
                    case TemplateSource.Local:
                        _current = BuildLocalPathStep();
                        break;
                }
            });
    }

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

    private void HandleGitRepoFetched(string cloneDir)
    {
        var dockerfiles = RepoService.FindDockerfiles(cloneDir);
        if (dockerfiles.Count == 0)
        {
            _current = new MessageStep("No Dockerfiles found in the repository.", _ => _current = BuildSourceSelectStep());
            return;
        }

        _current = new SingleSelectEditorScreen<string>(
            "Select a Dockerfile",
            dockerfiles,
            MarkupText.Escape,
            (context, dockerfile) =>
            {
                var absolutePath = Path.Combine(cloneDir, dockerfile);
                var config = new TemplateConfig(TemplateSource.GitRepo, GenerateImageName(), absolutePath, cloneDir, _pendingBranch);
                _onConfirm(context, config);
            });
    }

    private static string GenerateImageName() => $"create-sbx-{Guid.NewGuid().ToString("N")[..8]}";

    public override void OnLeave(ApplicationContext context) => _activeJob?.Cancel();

    public override void OnMessage(ApplicationContext context, ApplicationMessage message)
    {
        if (_current is BusyStep)
        {
            switch (message)
            {
                case RepoFetchSucceededMessage success:
                    HandleGitRepoFetched(success.CloneDir);
                    return;
                case RepoFetchFailedMessage failure:
                    _current = new MessageStep($"Failed to fetch repository: {failure.Error}", _ => _current = BuildSourceSelectStep());
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
