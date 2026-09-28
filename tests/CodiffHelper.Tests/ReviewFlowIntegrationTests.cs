using CodiffHelper.Tests.Support;

namespace CodiffHelper.Tests;

/// <summary>
/// Whole open / reopen / done cycles with real git: a local bare repository stands in for GitHub, and the
/// pull request lives at refs/pull/7/head there, like a pull request from a fork.
/// </summary>
public class ReviewFlowIntegrationTests : IDisposable
{
    private const string Url = "https://github.com/acme/widgets/pull/7";
    private readonly Harness _h = new();
    private readonly string _remote;
    private readonly LocalGitHub _gitHub;
    private readonly string _folder;

    public ReviewFlowIntegrationTests()
    {
        var remoteParent = _h.Temp.Combine("github");
        Directory.CreateDirectory(remoteParent);
        _remote = GitRepo.CreateRemote(remoteParent);
        _gitHub = new LocalGitHub(_remote);
        _h.GitHubOverride = _gitHub;
        _h.GitOverride = new GitCli(new IsolatedGitRunner());
        _folder = _h.FolderFor(Harness.Pr("acme", "widgets", 7));
    }

    private string PushToPullRequest(string content) =>
        GitRepo.PushPullRequestCommit(_h.Temp.Combine("github"), _remote, 7, content);

    private string Head() => GitRepo.Run(_folder, "rev-parse", "HEAD");

    [Fact]
    public async Task Open_update_and_done_cycle()
    {
        var first = PushToPullRequest("v1");
        Assert.Equal(ExitCodes.Success, await _h.RunAsync(Url));
        Assert.Equal(first, Head());
        Assert.Equal(first, OwnershipRecord.TryRead(_folder, out _)!.LastSyncedHead);
        Assert.Empty(GitRepo.Run(_folder, "status", "--porcelain"));

        var second = PushToPullRequest("v2");
        Assert.Equal(ExitCodes.Success, await _h.RunAsync(Url));
        Assert.Equal(second, Head());
        Assert.Contains($"Updated acme/widgets#7 to the latest commits ({second[..7]})", _h.Out.ToString());

        Assert.Equal(ExitCodes.Success, await _h.RunAsync(Url, "--done"));
        Assert.False(Directory.Exists(_folder));
        Assert.True(Directory.Exists(_h.Root));
    }

    [Fact]
    public async Task Update_follows_a_force_pushed_pull_request()
    {
        PushToPullRequest("v1");
        await _h.RunAsync(Url);

        // Rewrite the pull request from main so the old head is no longer in its history.
        var rewrite = _h.Temp.Combine("rewrite");
        GitRepo.Run(_h.Temp.Path, "clone", "--quiet", _remote, rewrite);
        GitRepo.Run(rewrite, "commit", "--quiet", "--allow-empty", "-m", "rewritten");
        GitRepo.Run(rewrite, "push", "--quiet", "--force", "origin", "HEAD:refs/pull/7/head");
        var rewritten = GitRepo.Run(rewrite, "rev-parse", "HEAD");

        await _h.RunAsync(Url);

        Assert.Equal(rewritten, Head());
        Assert.Equal(ExitCodes.Success, await _h.RunAsync(Url, "--done"));
    }

    [Fact]
    public async Task Edited_copy_is_kept_as_is_on_reopen_and_blocks_done()
    {
        var first = PushToPullRequest("v1");
        await _h.RunAsync(Url);
        File.AppendAllText(Path.Combine(_folder, "change.txt"), " edited");
        PushToPullRequest("v2");

        Assert.Equal(ExitCodes.Success, await _h.RunAsync(Url));

        Assert.Equal(first, Head());
        Assert.Contains("v1 edited", File.ReadAllText(Path.Combine(_folder, "change.txt")));
        Assert.Contains("has local edits or commits", _h.Error.ToString());

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync(Url, "--done"));
        Assert.Contains("1 changed file: change.txt", _h.Error.ToString());
        Assert.True(Directory.Exists(_folder));
    }

    [Fact]
    public async Task Local_commit_is_kept_on_reopen_and_needs_force_to_delete()
    {
        PushToPullRequest("v1");
        await _h.RunAsync(Url);
        GitRepo.Run(_folder, "commit", "--quiet", "--allow-empty", "-m", "reviewer experiment");
        var local = Head();
        PushToPullRequest("v2");

        await _h.RunAsync(Url);
        Assert.Equal(local, Head());

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync(Url, "--done"));
        Assert.Contains("reviewer experiment", _h.Error.ToString());

        Assert.Equal(ExitCodes.Success, await _h.RunAsync(Url, "--done", "--force"));
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task Untracked_file_blocks_done_from_inside_the_folder()
    {
        PushToPullRequest("v1");
        await _h.RunAsync(Url);
        File.WriteAllText(Path.Combine(_folder, "notes.md"), "my notes");
        _h.CurrentDirectory = _folder;

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync("--done"));
        Assert.Contains("1 untracked file: notes.md", _h.Error.ToString());

        Assert.Equal(ExitCodes.Success, await _h.RunAsync("--done", "--force"));
        Assert.False(Directory.Exists(_folder));
        Assert.Contains("Your shell is still in the deleted folder", _h.Out.ToString());
    }

    [Fact]
    public async Task Record_never_shows_as_a_working_tree_change()
    {
        PushToPullRequest("v1");
        await _h.RunAsync(Url);

        Assert.True(File.Exists(OwnershipRecord.PathIn(_folder)));
        Assert.Empty(GitRepo.Run(_folder, "status", "--porcelain", "--untracked-files=all"));
    }

    [Fact]
    public async Task List_reports_clean_and_changed_copies()
    {
        PushToPullRequest("v1");
        await _h.RunAsync(Url);
        _h.Out.GetStringBuilder().Clear();

        await _h.RunAsync("--list");
        Assert.Matches(@"acme/widgets#7\s+open\s+clean", _h.Out.ToString());

        File.WriteAllText(Path.Combine(_folder, "scratch.txt"), "x");
        _h.Out.GetStringBuilder().Clear();
        await _h.RunAsync("--list");
        Assert.Matches(@"acme/widgets#7\s+open\s+local changes", _h.Out.ToString());
    }

    public void Dispose() => _h.Dispose();
}
