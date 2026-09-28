using CodiffHelper.Tests.Support;

namespace CodiffHelper.Tests;

/// <summary>How gh and codiff are invoked, checked against a fake process runner.</summary>
public class ExternalCommandTests
{
    private static readonly PullRequestUrl Pr = new("acme", "widgets", 7);

    private static bool Is(ProcessRequest request, string file, params string[] prefix) =>
        request.FileName == file && request.Arguments.Take(prefix.Length).SequenceEqual(prefix);

    [Fact]
    public async Task Missing_gh_names_the_fix()
    {
        var runner = new FakeProcessRunner();
        runner.MissingCommands.Add("gh");
        var error = await Assert.ThrowsAsync<UserFacingException>(() => new GhCli(runner).EnsureReadyAsync());
        Assert.Contains("brew install gh", error.Message);
        Assert.Contains("gh auth login", error.Message);
    }

    [Fact]
    public async Task Signed_out_gh_names_the_fix()
    {
        var runner = new FakeProcessRunner().When(r => Is(r, "gh", "auth", "status"), new ProcessResult(1, "", "not logged in"));
        var error = await Assert.ThrowsAsync<UserFacingException>(() => new GhCli(runner).EnsureReadyAsync());
        Assert.Equal("gh is not signed in to GitHub; run: gh auth login", error.Message);
    }

    [Fact]
    public async Task Reads_pull_request_metadata()
    {
        var runner = new FakeProcessRunner().When(
            r => Is(r, "gh", "pr", "view"),
            new ProcessResult(0, """{"title":"Add widgets","state":"MERGED","headRefOid":"abc"}""", ""));

        var info = await new GhCli(runner).GetPullRequestAsync(Pr);

        Assert.Equal(new PullRequestInfo("Add widgets", "MERGED", "abc"), info);
        Assert.False(info.IsOpen);
        Assert.Equal(["pr", "view", "7", "--repo", "acme/widgets", "--json", "title,state,headRefOid"], runner.Requests.Single().Arguments);
        Assert.Equal("1", runner.Requests.Single().Environment!["GH_PROMPT_DISABLED"]);
    }

    [Fact]
    public async Task Unknown_pull_request_reports_gh_error_line()
    {
        var runner = new FakeProcessRunner().When(
            r => Is(r, "gh", "pr", "view"),
            new ProcessResult(1, "", "GraphQL: Could not resolve to a PullRequest with the number of 7. (repository.pullRequest)\n"));

        var error = await Assert.ThrowsAsync<UserFacingException>(() => new GhCli(runner).GetPullRequestAsync(Pr));
        Assert.Equal("could not load acme/widgets#7 from GitHub: GraphQL: Could not resolve to a PullRequest with the number of 7. (repository.pullRequest)", error.Message);
    }

    [Fact]
    public async Task Clone_is_a_blobless_partial_clone_through_gh()
    {
        var runner = new FakeProcessRunner();
        await new GhCli(runner).CloneAsync(Pr, "/tmp/dest");
        Assert.Equal(
            ["repo", "clone", "acme/widgets", "/tmp/dest", "--no-upstream", "--", "--filter=blob:none", "--quiet"],
            runner.Requests.Single().Arguments);
    }

    [Fact]
    public async Task Checkout_uses_gh_pr_checkout_on_a_fixed_branch()
    {
        var runner = new FakeProcessRunner();
        await new GhCli(runner).CheckoutAsync("/r/pr-7", Pr);

        var request = runner.Requests.Single();
        Assert.Equal("gh", request.FileName);
        Assert.Equal("/r/pr-7", request.WorkingDirectory);
        Assert.Equal(["pr", "checkout", "7", "--repo", "acme/widgets", "--branch", "pr-7", "--force"], request.Arguments);
    }

    [Fact]
    public async Task Checkout_falls_back_to_the_pull_request_ref_when_the_branch_is_gone()
    {
        var runner = new FakeProcessRunner().When(
            r => Is(r, "gh", "pr", "checkout"),
            new ProcessResult(1, "", "fatal: couldn't find remote ref refs/heads/feature\n"));

        await new GhCli(runner).CheckoutAsync("/r/pr-7", Pr);

        Assert.Equal(3, runner.Requests.Count);
        Assert.Equal(
            ["-c", "credential.helper=", "-c", "credential.helper=!gh auth git-credential", "fetch", "--quiet", "origin", "refs/pull/7/head"],
            runner.Requests[1].Arguments);
        Assert.Equal(["checkout", "--quiet", "-B", "pr-7", "FETCH_HEAD"], runner.Requests[2].Arguments);
    }

    [Fact]
    public async Task Checkout_failure_reports_the_gh_error()
    {
        var runner = new FakeProcessRunner()
            .When(r => Is(r, "gh", "pr", "checkout"), new ProcessResult(1, "", "fatal: couldn't find remote ref\n"))
            .When(r => r.FileName == "git", new ProcessResult(128, "", "fatal: no network\n"));

        var error = await Assert.ThrowsAsync<UserFacingException>(() => new GhCli(runner).CheckoutAsync("/r/pr-7", Pr));
        Assert.Equal("could not check out acme/widgets#7: fatal: couldn't find remote ref", error.Message);
    }

    [Theory]
    [InlineData("""{"title":"t"}""")]
    [InlineData("nope")]
    [InlineData("null")]
    public void Unreadable_gh_json_is_rejected(string json) => Assert.Null(GhCli.ParsePullRequest(json));

    [Fact]
    public async Task Codiff_is_launched_from_the_review_folder()
    {
        var runner = new FakeProcessRunner { LaunchExitCode = 0 };
        await new CodiffCli(runner, _ => null).LaunchAsync(new CodiffLaunch("/r/pr-7", Pr, Walkthrough: true, Agent: "pi"));

        var request = runner.Requests.Single();
        Assert.Equal("codiff", request.FileName);
        Assert.Equal("/r/pr-7", request.WorkingDirectory);
        Assert.Equal(["-w", "--agent", "pi", "https://github.com/acme/widgets/pull/7"], request.Arguments);
    }

    [Fact]
    public async Task Codiff_that_keeps_running_is_left_running()
    {
        var runner = new FakeProcessRunner { LaunchExitCode = null };
        await new CodiffCli(runner, _ => null).LaunchAsync(new CodiffLaunch("/r", Pr, false, null));
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task Codiff_failure_is_an_error()
    {
        var runner = new FakeProcessRunner { LaunchExitCode = 3 };
        var error = await Assert.ThrowsAsync<UserFacingException>(
            () => new CodiffCli(runner, _ => null).LaunchAsync(new CodiffLaunch("/r", Pr, false, null)));
        Assert.Equal("codiff exited with code 3 while opening acme/widgets#7", error.Message);
    }

    [Fact]
    public void Missing_codiff_names_the_fix()
    {
        using var temp = new TempDirectory();
        var codiff = new CodiffCli(new FakeProcessRunner(), name => name == "PATH" ? temp.Path : null);
        var error = Assert.Throws<UserFacingException>(codiff.EnsureInstalled);
        Assert.Contains("install Codiff (https://github.com/nkzw-tech/codiff)", error.Message);

        File.WriteAllText(Path.Combine(temp.Path, "codiff"), "#!/bin/sh\n");
        codiff.EnsureInstalled();
    }

    [Fact]
    public async Task Real_runner_reports_missing_commands_and_launch_behaviour()
    {
        var runner = new ProcessRunner();
        await Assert.ThrowsAsync<CommandNotFoundException>(
            () => runner.RunAsync(new ProcessRequest("codiff-helper-no-such-command", [])));

        Assert.Equal(0, await runner.LaunchAsync(new ProcessRequest("/usr/bin/true", []), TimeSpan.FromSeconds(10)));
        Assert.Null(await runner.LaunchAsync(new ProcessRequest("/bin/sleep", ["2"]), TimeSpan.FromMilliseconds(100)));

        var echoed = await runner.RunAsync(new ProcessRequest("/bin/sh", ["-c", "echo out; echo err >&2; exit 4"]));
        Assert.Equal(new ProcessResult(4, "out\n", "err\n"), echoed);
        Assert.Equal("err", echoed.ErrorSummary);
    }
}
