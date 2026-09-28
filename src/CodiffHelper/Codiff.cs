namespace CodiffHelper;

internal sealed record CodiffLaunch(string WorkingDirectory, PullRequestUrl PullRequest, bool Walkthrough, string? Agent)
{
    /// <summary>Arguments in the form <c>codiff [-w] [--agent &lt;name&gt;] &lt;url&gt;</c>.</summary>
    public IReadOnlyList<string> Arguments =>
    [
        .. Walkthrough ? ["-w"] : Array.Empty<string>(),
        .. Agent is null ? Array.Empty<string>() : ["--agent", Agent],
        PullRequest.Url,
    ];
}

/// <summary>Seam over the Codiff desktop app's <c>codiff</c> command.</summary>
internal interface ICodiff
{
    /// <summary>Fails with a message naming the fix when the codiff command is missing.</summary>
    void EnsureInstalled();

    /// <summary>Opens Codiff and returns promptly whether or not the command waits for its window.</summary>
    Task LaunchAsync(CodiffLaunch launch, CancellationToken cancellationToken = default);
}

internal sealed class CodiffCli(IProcessRunner runner, Func<string, string?> getEnvironmentVariable) : ICodiff
{
    public const string Command = "codiff";

    private const string InstallHint =
        "the codiff command was not found; install Codiff (https://github.com/nkzw-tech/codiff) and make sure its codiff command is on your PATH";

    /// <summary>
    /// codiff 1.14 hands off to the app with <c>open -n</c> and exits at once; anything still running after this
    /// long is treated as a window-bound command and left running.
    /// </summary>
    private static readonly TimeSpan LaunchWait = TimeSpan.FromSeconds(10);

    public void EnsureInstalled()
    {
        if (FindOnPath(Command, getEnvironmentVariable("PATH")) is null) throw new UserFacingException(InstallHint);
    }

    public async Task LaunchAsync(CodiffLaunch launch, CancellationToken cancellationToken = default)
    {
        int? exitCode;
        try
        {
            exitCode = await runner.LaunchAsync(
                new ProcessRequest(Command, launch.Arguments, launch.WorkingDirectory),
                LaunchWait,
                cancellationToken).ConfigureAwait(false);
        }
        catch (CommandNotFoundException)
        {
            throw new UserFacingException(InstallHint);
        }

        if (exitCode is { } code && code != 0)
            throw new UserFacingException($"codiff exited with code {code} while opening {launch.PullRequest.DisplayName}");
    }

    internal static string? FindOnPath(string command, string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, command);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
