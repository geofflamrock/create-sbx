namespace CreateSbx.Messages;

public sealed record SbxProcessFinishedMessage(int ExitCode) : ApplicationMessage;
