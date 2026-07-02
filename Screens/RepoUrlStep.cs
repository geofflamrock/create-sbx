using CreateSbx.Models;
using CreateSbx.Services;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Shared "pick a recent URL or enter a new one, then enter a branch" sub-flow used by
/// both the Template (git repository) and Kits fields. On success, calls
/// <paramref name="onResolved"/> with the parsed owner/repo/branch.</summary>
internal sealed class RepoUrlStep : IStep
{
    private const string EnterUrlSentinel = "Enter URL";

    private readonly SandboxConfig _config;
    private readonly string _purpose;
    private readonly Action<ApplicationContext, string, string, string> _onResolved;
    private IStep _current;

    public RepoUrlStep(SandboxConfig config, string purpose, Action<ApplicationContext, string, string, string> onResolved)
    {
        _config = config;
        _purpose = purpose;
        _onResolved = onResolved;
        _current = BuildUrlSelectStep();
    }

    private IStep BuildUrlSelectStep()
    {
        if (_config.RecentUrls.Count == 0)
        {
            return BuildUrlTextStep(string.Empty);
        }

        var options = new List<string>(_config.RecentUrls) { EnterUrlSentinel };
        return new SingleSelectEditorScreen<string>(
            $"Select a {_purpose} repository URL",
            options,
            url => url == EnterUrlSentinel ? "[grey]Enter URL…[/]" : MarkupText.Escape(url),
            (context, chosen) =>
            {
                if (chosen == EnterUrlSentinel)
                {
                    _current = BuildUrlTextStep(string.Empty);
                    return;
                }

                OnUrlChosen(context, chosen);
            });
    }

    private IStep BuildUrlTextStep(string initialValue) =>
        new TextFieldEditorScreen(
            $"Enter the GitHub repository URL for the {_purpose}",
            initialValue,
            (context, url) => OnUrlChosen(context, url.Trim().TrimEnd('/')),
            placeholder: "https://github.com/owner/repo",
            validate: url =>
            {
                var (owner, repo) = GitHubUrlParser.Parse(url);
                return owner is null || repo is null
                    ? "Invalid GitHub repository URL. Expected format: https://github.com/owner/repo"
                    : null;
            });

    private void OnUrlChosen(ApplicationContext context, string url)
    {
        var (owner, repo) = GitHubUrlParser.Parse(url);
        if (owner is null || repo is null)
        {
            _current = BuildUrlTextStep(url);
            return;
        }

        RecentUrlsStore.Add(_config.RecentUrls, url);

        _current = new TextFieldEditorScreen(
            "Enter branch (leave blank for default)",
            "",
            (context2, branch) => _onResolved(context2, owner, repo, branch));
    }

    public void OnMessage(ApplicationContext context, ApplicationMessage message) => _current.OnMessage(context, message);

    public void Render(RenderContext context) => _current.Render(context);
}
