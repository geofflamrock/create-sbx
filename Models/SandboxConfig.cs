namespace CreateSbx.Models;

/// <summary>Mutable session state: the fields the user is editing plus session-scoped caches
/// (recent URLs, already-fetched repos) shared across every field editor.</summary>
public sealed class SandboxConfig
{
    public static readonly IReadOnlyList<AgentOption> BuiltInAgents =
    [
        new("claude", "Claude Code", null),
        new("codex", "Codex", null),
        new("copilot", "Copilot", null),
        new("cursor", "Cursor", null),
        new("droid", "Droid", null),
        new("gemini", "Gemini", null),
        new("kiro", "Kiro", null),
        new("opencode", "OpenCode", null),
        new("docker-agent", "Docker Agent", null),
        new("shell", "Shell", "Agent-less sandbox for manual setup or testing"),
    ];

    public static readonly IReadOnlyList<WorkspaceModeOption> WorkspaceModes =
    [
        new("Direct", "Mount the host directory directly into the sandbox", false),
        new("Clone", "Clone the repository into the sandbox", true),
    ];

    public string Name { get; set; } = new DirectoryInfo(Directory.GetCurrentDirectory()).Name;
    public string AgentId { get; set; } = BuiltInAgents[0].Id;
    public string WorkDir { get; set; } = ".";
    public WorkspaceModeOption WorkspaceMode { get; set; } = WorkspaceModes[0];
    public TemplateConfig? Template { get; set; }
    public List<KitGroup> KitGroups { get; } = [];

    public List<string> RecentUrls { get; }
    public HashSet<string> FetchedRepos { get; } = [];

    public SandboxConfig(List<string> recentUrls)
    {
        RecentUrls = recentUrls;
    }
}
