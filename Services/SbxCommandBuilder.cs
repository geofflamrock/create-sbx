using CreateSbx.Models;

namespace CreateSbx.Services;

public static class SbxCommandBuilder
{
    /// <summary>The template name to show in the preview before a Dockerfile-based template has
    /// been built (its real image name isn't known until then).</summary>
    public static string? GetDisplayTemplateName(TemplateConfig? template) =>
        template?.Source is TemplateSource.GitRepo or TemplateSource.Local
            ? "<image-id>"
            : template?.ImageName;

    public static List<string> BuildArgs(SandboxConfig config, string? effectiveTemplateName)
    {
        var args = new List<string> { "create", "--name", config.Name };

        if (effectiveTemplateName is not null)
        {
            args.Add("--template");
            args.Add(effectiveTemplateName);
        }

        foreach (var kitGroup in config.KitGroups)
        {
            foreach (var kitUrl in kitGroup.BuildKitUrls())
            {
                args.Add("--kit");
                args.Add(kitUrl);
            }
        }

        if (config.WorkspaceMode.UseClone)
        {
            args.Add("--clone");
        }

        args.Add(config.AgentId);
        args.Add(config.WorkDir);

        return args;
    }

    public static string BuildDisplayCommand(SandboxConfig config, string? templateName)
    {
        var parts = new List<string> { "sbx create", $"--name \"{config.Name}\"" };

        if (templateName is not null)
        {
            parts.Add($"--template \"{templateName}\"");
        }

        var kitUrls = config.KitGroups.SelectMany(g => g.BuildKitUrls()).ToList();
        if (kitUrls.Count > 0)
        {
            parts.Add(string.Join(" ", kitUrls.Select(u => $"--kit \"{u}\"")));
        }

        if (config.WorkspaceMode.UseClone)
        {
            parts.Add("--clone");
        }

        parts.Add(config.AgentId);
        parts.Add($"\"{config.WorkDir}\"");

        return string.Join(" ", parts);
    }
}
