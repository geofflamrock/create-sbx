namespace CreateSbx.Messages;

public sealed record RepoFetchFailedMessage(string Error) : ApplicationMessage;
