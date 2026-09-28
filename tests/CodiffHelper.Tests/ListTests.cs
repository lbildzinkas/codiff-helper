using CodiffHelper.Tests.Support;

namespace CodiffHelper.Tests;

public class ListTests : IDisposable
{
    private readonly Harness _h = new();

    [Fact]
    public async Task Empty_or_missing_root_says_so()
    {
        Assert.Equal(ExitCodes.Success, await _h.RunAsync("--list"));
        Assert.Contains("No review copies in ~/Reviews", _h.Out.ToString());
    }

    [Fact]
    public async Task Shows_each_review_with_title_state_and_local_changes()
    {
        var clean = _h.PlantReview(Harness.Pr("acme", "web", 7));
        var dirty = _h.PlantReview(Harness.Pr("acme", "api", 21));
        _h.PlantReview(Harness.Pr("acme", "api", 22), withRecord: false);
        _h.GitHub.PullRequests[7] = new PullRequestInfo("Add login page", "OPEN", "");
        _h.GitHub.PullRequests[21] = new PullRequestInfo("Speed up search", "MERGED", "");
        _h.Git.States[dirty] = new LocalState([], ["scratch.txt"], [], 0);

        Assert.Equal(ExitCodes.Success, await _h.RunAsync("--list"));

        var lines = _h.Out.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Matches(@"^REVIEW\s+STATE\s+LOCAL\s+TITLE\s+PATH$", lines[0]);
        Assert.Matches(@"^acme/api#21\s+merged\s+local changes\s+Speed up search\s+~/Reviews/acme/api/pr-21$", lines[1]);
        Assert.Matches(@"^acme/web#7\s+open\s+clean\s+Add login page\s+~/Reviews/acme/web/pr-7$", lines[2]);
        Assert.True(Directory.Exists(clean));
    }

    [Fact]
    public async Task Falls_back_to_the_saved_title_when_GitHub_cannot_answer()
    {
        _h.PlantReview(Harness.Pr("acme", "web", 7));
        _h.GitHub.LookupFailures.Add(7);

        await _h.RunAsync("--list");

        Assert.Matches(@"acme/web#7\s+open\s+clean\s+Planted", _h.Out.ToString());
    }

    [Fact]
    public async Task Works_without_gh_using_saved_titles()
    {
        _h.PlantReview(Harness.Pr("acme", "web", 7));
        _h.GitHub.NotReadyMessage = "gh is not signed in to GitHub; run: gh auth login";

        Assert.Equal(ExitCodes.Success, await _h.RunAsync("--list"));

        Assert.Matches(@"acme/web#7\s+open\s+clean\s+Planted", _h.Out.ToString());
        Assert.Contains("note: showing saved titles and states", _h.Error.ToString());
    }

    [Fact]
    public async Task Unreadable_git_state_shows_unknown()
    {
        var folder = _h.PlantReview(Harness.Pr("acme", "web", 7));
        _h.Git.Broken.Add(folder);

        await _h.RunAsync("--list");

        Assert.Matches(@"acme/web#7\s+open\s+unknown", _h.Out.ToString());
    }

    [Fact]
    public async Task List_never_deletes_or_changes_anything()
    {
        var folder = _h.PlantReview(Harness.Pr("acme", "web", 7));
        var before = File.ReadAllText(OwnershipRecord.PathIn(folder));

        await _h.RunAsync("--list");

        Assert.Equal(before, File.ReadAllText(OwnershipRecord.PathIn(folder)));
        Assert.Empty(_h.GitHub.Checkouts);
        Assert.Empty(_h.Codiff.Launches);
    }

    public void Dispose() => _h.Dispose();
}
