using CodiffHelper.Tests.Support;

namespace CodiffHelper.Tests;

/// <summary>Real git against local temporary repositories; no network and nothing outside the temp folder.</summary>
public class GitIntegrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly GitCli _git = new(new IsolatedGitRunner());
    private readonly string _clone;
    private readonly string _pullRequestHead;

    public GitIntegrationTests()
    {
        var remote = GitRepo.CreateRemote(_temp.Path);
        _pullRequestHead = GitRepo.PushPullRequestCommit(_temp.Path, remote, 1, "one");
        _clone = _temp.Combine("clone");
        GitRepo.Run(_temp.Path, "clone", "--quiet", remote, _clone);
        GitRepo.Run(_clone, "fetch", "--quiet", "origin", "refs/pull/1/head");
        GitRepo.Run(_clone, "checkout", "--quiet", "-B", "pr-1", "FETCH_HEAD");
    }

    private Task<LocalState> State() => _git.GetLocalStateAsync(_clone, [_pullRequestHead]);

    [Fact]
    public async Task Fresh_checkout_is_clean()
    {
        var state = await State();
        Assert.False(state.HasLocalWork);
        Assert.Equal(_pullRequestHead, await _git.GetHeadCommitAsync(_clone));
    }

    [Fact]
    public async Task Pull_request_head_without_a_remote_branch_counts_as_local_unless_known()
    {
        var unknown = await _git.GetLocalStateAsync(_clone, []);
        Assert.Single(unknown.UnpushedCommits);
        Assert.False((await State()).HasLocalWork);
    }

    [Fact]
    public async Task Detects_changed_files()
    {
        File.AppendAllText(Path.Combine(_clone, "README.md"), "more\n");
        var state = await State();
        Assert.Equal(["README.md"], state.ChangedFiles);
        Assert.True(state.HasWorkingTreeChanges);
    }

    [Fact]
    public async Task Detects_staged_renames_once()
    {
        GitRepo.Run(_clone, "mv", "README.md", "RENAMED.md");
        Assert.Equal(["RENAMED.md"], (await State()).ChangedFiles);
    }

    [Fact]
    public async Task Detects_untracked_files_but_not_ignored_ones()
    {
        File.WriteAllText(Path.Combine(_clone, ".git", "info", "exclude"), "*.log\n");
        File.WriteAllText(Path.Combine(_clone, "build.log"), "ignored");
        File.WriteAllText(Path.Combine(_clone, "notes with space.md"), "x");

        var state = await State();
        Assert.Equal(["notes with space.md"], state.UntrackedFiles);
        Assert.Empty(state.ChangedFiles);
    }

    [Fact]
    public async Task Untracked_files_are_found_even_when_config_hides_them()
    {
        GitRepo.Run(_clone, "config", "status.showUntrackedFiles", "no");
        File.WriteAllText(Path.Combine(_clone, "hidden.txt"), "x");
        Assert.Equal(["hidden.txt"], (await State()).UntrackedFiles);
    }

    [Fact]
    public async Task Detects_local_commits_on_the_current_branch()
    {
        File.WriteAllText(Path.Combine(_clone, "fix.txt"), "fix");
        GitRepo.Run(_clone, "add", ".");
        GitRepo.Run(_clone, "commit", "--quiet", "-m", "my local fix");

        var state = await State();
        var commit = Assert.Single(state.UnpushedCommits);
        Assert.EndsWith(" my local fix", commit);
        Assert.False(state.HasWorkingTreeChanges);
    }

    [Fact]
    public async Task Detects_local_commits_on_other_branches()
    {
        GitRepo.Run(_clone, "checkout", "--quiet", "-b", "side");
        GitRepo.Run(_clone, "commit", "--quiet", "--allow-empty", "-m", "side work");
        GitRepo.Run(_clone, "checkout", "--quiet", "pr-1");

        Assert.Single((await State()).UnpushedCommits);
    }

    [Fact]
    public async Task Detects_a_detached_local_commit()
    {
        GitRepo.Run(_clone, "checkout", "--quiet", "--detach");
        GitRepo.Run(_clone, "commit", "--quiet", "--allow-empty", "-m", "detached work");

        Assert.Single((await State()).UnpushedCommits);
    }

    [Fact]
    public async Task Commits_on_remote_branches_are_not_local()
    {
        GitRepo.Run(_clone, "checkout", "--quiet", "main");
        GitRepo.Run(_clone, "branch", "--quiet", "-D", "pr-1");
        Assert.Empty((await _git.GetLocalStateAsync(_clone, [])).UnpushedCommits);
    }

    [Fact]
    public async Task Detects_stashes()
    {
        File.AppendAllText(Path.Combine(_clone, "README.md"), "stash me\n");
        GitRepo.Run(_clone, "stash", "--quiet");

        var state = await State();
        Assert.Equal(1, state.StashCount);
        Assert.False(state.HasWorkingTreeChanges);
        Assert.True(state.HasLocalWork);
    }

    [Fact]
    public async Task Unknown_or_malformed_known_commits_are_ignored()
    {
        var state = await _git.GetLocalStateAsync(_clone, [_pullRequestHead, "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", "--all", "HEAD"]);
        Assert.False(state.HasLocalWork);
    }

    [Fact]
    public async Task Not_a_repository_is_an_error()
    {
        var plain = _temp.Combine("plain");
        Directory.CreateDirectory(plain);
        await Assert.ThrowsAsync<UserFacingException>(() => _git.GetLocalStateAsync(plain, []));
        Assert.Null(await _git.GetHeadCommitAsync(plain));
    }

    [Fact]
    public void Parses_porcelain_status_with_renames()
    {
        var (changed, untracked) = GitCli.ParseStatus(" M a.txt\0R  new.txt\0old.txt\0?? dir/\0A  added.txt\0");
        Assert.Equal(["a.txt", "new.txt", "added.txt"], changed);
        Assert.Equal(["dir/"], untracked);
    }

    public void Dispose() => _temp.Dispose();
}
