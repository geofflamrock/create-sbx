using System.Diagnostics;

namespace CreateSbx.Services;

/// <summary>Runs child processes for the TUI. A fullscreen/inline terminal app can't hand the
/// terminal to an interactive child process, so output is always redirected — either buffered
/// (<see cref="RunAsync"/>/<see cref="RunAndCaptureAsync"/>) or streamed line-by-line
/// (<see cref="RunStreamingAsync"/>) so callers can forward it into the UI as it arrives.</summary>
public static class ProcessRunner
{
    public static async Task RunAsync(string fileName, IReadOnlyList<string> args, string? workDir = null)
    {
        using var process = Start(fileName, args, workDir, redirect: true);
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException((await errorTask).Trim());
        }
    }

    public static async Task<string> RunAndCaptureAsync(string fileName, IReadOnlyList<string> args, string? workDir = null)
    {
        using var process = Start(fileName, args, workDir, redirect: true);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException((await errorTask).Trim());
        }

        return (await outputTask).Trim();
    }

    /// <summary>Runs a process, forwarding each stdout/stderr line to <paramref name="onLine"/>
    /// as it arrives. Returns the exit code; when <paramref name="throwOnNonZeroExit"/> is true
    /// (the default) a non-zero exit throws instead, for callers that treat failure as fatal.</summary>
    public static async Task<int> RunStreamingAsync(
        string fileName,
        IReadOnlyList<string> args,
        Action<string> onLine,
        string? workDir = null,
        bool throwOnNonZeroExit = true,
        CancellationToken cancellationToken = default)
    {
        using var process = Start(fileName, args, workDir, redirect: true);

        var stdoutTask = PumpLinesAsync(process.StandardOutput, onLine, cancellationToken);
        var stderrTask = PumpLinesAsync(process.StandardError, onLine, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask);

        if (throwOnNonZeroExit && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}");
        }

        return process.ExitCode;
    }

    private static async Task PumpLinesAsync(StreamReader reader, Action<string> onLine, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            onLine(line);
        }
    }

    private static Process Start(string fileName, IReadOnlyList<string> args, string? workDir, bool redirect)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            UseShellExecute = false,
        };

        if (workDir != null)
        {
            psi.WorkingDirectory = workDir;
        }

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
    }
}
