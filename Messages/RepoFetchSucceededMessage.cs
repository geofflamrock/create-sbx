namespace CreateSbx.Messages;

public sealed record RepoFetchSucceededMessage(string CloneDir) : ApplicationMessage;
