namespace CreateSbx.Models;

public sealed record TemplateConfig(
    TemplateSource Source,
    string ImageName,
    string? DockerfilePath,
    string? DockerContext,
    string? Branch = null);
