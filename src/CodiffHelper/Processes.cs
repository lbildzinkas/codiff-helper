using System.ComponentModel;
using System.Diagnostics;

namespace CodiffHelper;

internal sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null);

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>The most useful single line of error output, for one-line messages.</summary>
    public string ErrorSummary
    {
        get
        {
            var lines = (StandardError + "\n" + StandardOutput)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var line = lines.FirstOrDefault(l => l.StartsWith("fatal:", StringComparison.Ordinal)
                                                 || l.StartsWith("error:", StringComparison.Ordinal)
                                                 || l.StartsWith("GraphQL:", StringComparison.Ordinal))
                       ?? lines.FirstOrDefault();
            return line ?? $"exit code {ExitCode}";
        }
    }
}

/// <summary>Raised when an external command is not installed or not on PATH.</summary>
internal sealed class CommandNotFoundException(string command)
    : Exception($"'{command}' was not found on PATH")
{
    public string Command { get; } = command;
}

/// <summary>Seam over process execution so gh, git and codiff can be faked in tests.</summary>
internal interface IProcessRunner
{
    /// <summary>Runs a command to completion, capturing its output.</summary>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a command that shares this terminal and waits at most <paramref name="wait"/> for it to exit.
    /// Returns its exit code, or null when it is still running (it is then left running).
    /// </summary>
    Task<int?> LaunchAsync(ProcessRequest request, TimeSpan wait, CancellationToken cancellationToken = default);
}

internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = CreateStartInfo(request, redirect: true) };
        Start(process, request.FileName);
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stop the command before the caller cleans up after it.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    public async Task<int?> LaunchAsync(ProcessRequest request, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = CreateStartInfo(request, redirect: false) };
        Start(process, request.FileName);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(wait);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessRequest request, bool redirect)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = redirect,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
        };
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);
        if (request.WorkingDirectory is not null) startInfo.WorkingDirectory = request.WorkingDirectory;
        if (request.Environment is null) return startInfo;

        foreach (var (key, value) in request.Environment)
        {
            if (value is null) startInfo.Environment.Remove(key);
            else startInfo.Environment[key] = value;
        }
        return startInfo;
    }

    private static void Start(Process process, string fileName)
    {
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new CommandNotFoundException(fileName);
        }
    }
}
