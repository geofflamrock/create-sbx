namespace CreateSbx.Messages;

/// <summary>A single line for the preview panel's log: status updates, streamed process output, errors.</summary>
public sealed record LogMessage(string Text) : ApplicationMessage;
