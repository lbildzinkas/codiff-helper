namespace CodiffHelper;

/// <summary>Work in a review copy that exists only on this machine.</summary>
internal sealed record LocalState(
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> UntrackedFiles,
    IReadOnlyList<string> UnpushedCommits,
    int StashCount)
{
    public static LocalState Clean { get; } = new([], [], [], 0);

    /// <summary>True when the working tree has edits or untracked files.</summary>
    public bool HasWorkingTreeChanges => ChangedFiles.Count > 0 || UntrackedFiles.Count > 0;

    /// <summary>True when anything here would be lost by deleting the copy.</summary>
    public bool HasLocalWork => HasWorkingTreeChanges || UnpushedCommits.Count > 0 || StashCount > 0;
}

/// <summary>Seam over local-only git queries.</summary>
internal interface IGit
{
    /// <summary>
    /// Reports uncommitted changes, untracked files, stashes and commits reachable from HEAD or a local
    /// branch that are in neither a remote-tracking branch nor <paramref name="knownRemoteCommits"/>.
    /// </summary>
    Task<LocalState> GetLocalStateAsync(
        string repositoryPath,
        IReadOnlyCollection<string> knownRemoteCommits,
        CancellationToken cancellationToken = default);

    /// <summary>The commit HEAD points to, or null when there is none.</summary>
    Task<string?> GetHeadCommitAsync(string repositoryPath, CancellationToken cancellationToken = default);
}

internal sealed class GitCli(IProcessRunner runner) : IGit
{
    public async Task<LocalState> GetLocalStateAsync(
        string repositoryPath,
        IReadOnlyCollection<string> knownRemoteCommits,
        CancellationToken cancellationToken = default)
    {
        var status = await RunAsync(
            repositoryPath,
            ["status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignore-submodules=none"],
            cancellationToken).ConfigureAwait(false);
        var (changed, untracked) = ParseStatus(status.StandardOutput);

        string[] logArguments =
        [
            "log", "--format=%h %s", "--ignore-missing", "HEAD", "--branches", "--not", "--remotes",
            .. knownRemoteCommits.Where(IsCommitId),
        ];
        var log = await RunAsync(repositoryPath, logArguments, cancellationToken).ConfigureAwait(false);
        var unpushed = log.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var stash = await RunAsync(repositoryPath, ["stash", "list", "--format=%gd"], cancellationToken).ConfigureAwait(false);
        var stashCount = stash.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        return new LocalState(changed, untracked, unpushed, stashCount);
    }

    public async Task<string?> GetHeadCommitAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync(
            new ProcessRequest("git", ["rev-parse", "--verify", "--quiet", "HEAD"], repositoryPath),
            cancellationToken).ConfigureAwait(false);
        var head = result.StandardOutput.Trim();
        return result.Succeeded && IsCommitId(head) ? head : null;
    }

    /// <summary>Parses <c>git status --porcelain=v1 -z</c> into changed and untracked paths.</summary>
    internal static (List<string> Changed, List<string> Untracked) ParseStatus(string output)
    {
        var changed = new List<string>();
        var untracked = new List<string>();
        var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 4) continue;

            var code = entry[..2];
            var path = entry[3..];
            if (code == "??") untracked.Add(path);
            else if (code != "!!") changed.Add(path);

            // Renames and copies are followed by their original path as a separate entry.
            if (code[0] is 'R' or 'C') i++;
        }
        return (changed, untracked);
    }

    private static bool IsCommitId(string value) =>
        value.Length is >= 7 and <= 64 && value.All(char.IsAsciiHexDigit);

    private async Task<ProcessResult> RunAsync(string repositoryPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            new ProcessRequest("git", ["-c", "core.quotePath=false", .. arguments], repositoryPath),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new UserFacingException($"git {arguments[0]} failed in {repositoryPath}: {result.ErrorSummary}");
        return result;
    }
}
