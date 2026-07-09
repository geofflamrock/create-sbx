namespace CreateSbx.Models;

/// <summary>An additional directory to mount into the sandbox, beyond the main workspace
/// directory.</summary>
public sealed class WorkspaceDirectory
{
    public required string Path { get; set; }
    public required bool ReadOnly { get; set; }

    public string ToArgument() => ReadOnly ? $"{Path}:ro" : Path;
}
