using CreateSbx.Models;

namespace CreateSbx.Services;

public static class DockerService
{
    public static async Task<string> BuildAndLoadDockerImageAsync(TemplateConfig template, Action<string> onLine)
    {
        var imagesDir = Path.Combine(Path.GetTempPath(), "create-sbx", "images");
        Directory.CreateDirectory(imagesDir);

        if (template.Source == TemplateSource.GitRepo)
        {
            if (string.IsNullOrEmpty(template.Branch))
            {
                await ProcessRunner.RunAsync("git", ["checkout", "--detach", "origin/HEAD"], template.DockerContext);
            }
            else
            {
                await ProcessRunner.RunAsync("git", ["checkout", "--detach", $"origin/{template.Branch}"], template.DockerContext);
            }
        }

        onLine($"Building Dockerfile {template.DockerfilePath}...");
        await ProcessRunner.RunStreamingAsync(
            "docker",
            ["build", "-t", template.ImageName, "-f", template.DockerfilePath!, template.DockerContext!],
            onLine);

        var imageId = await GetDockerImageIdAsync(template.ImageName);
        var shortHash = imageId.StartsWith("sha256:") ? imageId[7..19] : imageId[..12];
        var stableImageName = $"create-sbx-{shortHash}";
        var tarPath = Path.Combine(imagesDir, $"{stableImageName}.tar");

        if (!File.Exists(tarPath))
        {
            await ProcessRunner.RunAsync("docker", ["tag", template.ImageName, stableImageName]);

            onLine("Saving Docker image...");
            await ProcessRunner.RunAsync("docker", ["save", stableImageName, "-o", tarPath]);

            onLine("Docker image built successfully.");
            onLine("Loading template into sbx...");
            await ProcessRunner.RunStreamingAsync("sbx", ["template", "load", tarPath], onLine);
            onLine("Template loaded into sbx.");
        }
        else
        {
            onLine($"Docker image built. Using cached template {stableImageName}.");
        }

        return stableImageName;
    }

    private static Task<string> GetDockerImageIdAsync(string imageName) =>
        ProcessRunner.RunAndCaptureAsync("docker", ["inspect", "--format={{.Id}}", imageName]);
}
