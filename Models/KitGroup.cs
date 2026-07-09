namespace CreateSbx.Models;

/// <summary>One added kit source: a fetched repo/branch plus the kits selected from it.</summary>
public sealed class KitGroup
{
    public required string Owner { get; init; }
    public required string Repo { get; init; }
    public required string Branch { get; init; }
    public required List<Kit> SelectedKits { get; set; }

    public string RepoUrl => $"git+https://github.com/{Owner}/{Repo}.git";

    public IEnumerable<string> BuildKitUrls()
    {
        var refFragment = string.IsNullOrEmpty(Branch) ? "" : $"&ref={Uri.EscapeDataString(Branch)}";
        foreach (var kit in SelectedKits)
        {
            yield return kit.Directory is not null
                ? $"{RepoUrl}#dir={kit.Directory}{refFragment}"
                : string.IsNullOrEmpty(refFragment) ? RepoUrl : $"{RepoUrl}#{refFragment.TrimStart('&')}";
        }
    }
}
