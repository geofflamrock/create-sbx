using System.Text.RegularExpressions;

namespace CreateSbx.Services;

public static partial class GitHubUrlParser
{
    public static (string? Owner, string? Repo) Parse(string url)
    {
        var match = Regex().Match(url);
        if (!match.Success)
        {
            return (null, null);
        }

        return (match.Groups["owner"].Value, match.Groups["repo"].Value);
    }

    [GeneratedRegex(@"github\.com[/:](?<owner>[^/]+)/(?<repo>[^/.]+)")]
    private static partial Regex Regex();
}
