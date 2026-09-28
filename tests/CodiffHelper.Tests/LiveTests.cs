using CodiffHelper.Tests.Support;

namespace CodiffHelper.Tests;

/// <summary>Runs only when CODIFF_HELPER_LIVE_TESTS=1; needs network access and a signed-in gh.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public const string Variable = "CODIFF_HELPER_LIVE_TESTS";

    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1") Skip = $"set {Variable}=1 to run live GitHub tests";
    }
}

/// <summary>
/// Clones a small public pull request into a temporary root with the real gh and git. Codiff is faked, so the
/// app is never launched.
/// </summary>
public class LiveTests : IDisposable
{
    private readonly Harness _h = new();

    public LiveTests()
    {
        var runner = new ProcessRunner();
        _h.GitOverride = new GitCli(runner);
        _h.GitHubOverride = new GhCli(runner);
    }

    [LiveFact]
    public async Task Opens_lists_and_deletes_a_public_pull_request()
    {
        const string url = "https://github.com/octocat/Hello-World/pull/6";
        var folder = _h.FolderFor(Harness.Pr("octocat", "Hello-World", 6));

        Assert.Equal(ExitCodes.Success, await _h.RunAsync(url));
        Assert.True(Directory.Exists(Path.Combine(folder, ".git")));
        Assert.Contains("note: octocat/Hello-World#6 is merged", _h.Out.ToString());
        Assert.Equal(folder, Assert.Single(_h.Codiff.Launches).WorkingDirectory);

        Assert.Equal(ExitCodes.Success, await _h.RunAsync(url));
        Assert.Contains("is already up to date", _h.Out.ToString());

        Assert.Equal(ExitCodes.Success, await _h.RunAsync("--list"));
        Assert.Matches(@"octocat/Hello-World#6\s+merged\s+clean", _h.Out.ToString());

        Assert.Equal(ExitCodes.Success, await _h.RunAsync(url, "--done"));
        Assert.False(Directory.Exists(folder));
    }

    public void Dispose() => _h.Dispose();
}
