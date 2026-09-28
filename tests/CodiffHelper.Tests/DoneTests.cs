using CodiffHelper.Tests.Support;

namespace CodiffHelper.Tests;

public class DoneTests : IDisposable
{
    private const string Url = "https://github.com/Owner/Repo/pull/12";
    private readonly Harness _h = new();
    private readonly PullRequestUrl _pr = Harness.Pr("Owner", "Repo", 12);

    public static TheoryData<string, string> Blockers => new()
    {
        { "changed", "2 changed files: src/a.cs, b.txt" },
        { "untracked", "1 untracked file: notes.md" },
        { "unpushed", "1 local commit not on GitHub: abc1234 my fix" },
        { "stash", "2 stash entries" },
    };

    private static LocalState StateFor(string kind) => kind switch
    {
        "changed" => new LocalState(["src/a.cs", "b.txt"], [], [], 0),
        "untracked" => new LocalState([], ["notes.md"], [], 0),
        "unpushed" => new LocalState([], [], ["abc1234 my fix"], 0),
        "stash" => new LocalState([], [], [], 2),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(Blockers))]
    public async Task Refuses_when_work_is_not_on_GitHub(string kind, string expected)
    {
        var folder = _h.PlantReview(_pr);
        _h.Git.States[folder] = StateFor(kind);

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync(Url, "--done"));

        Assert.True(Directory.Exists(folder), kind);
        var error = _h.Error.ToString();
        Assert.Contains("it has work that is not on GitHub", error);
        Assert.Contains(expected, error);
        Assert.Contains($"codiff-helper {Url} --done --force", error);
    }

    [Theory]
    [MemberData(nameof(Blockers))]
    public async Task Force_deletes_anyway(string kind, string expected)
    {
        var folder = _h.PlantReview(_pr);
        _h.Git.States[folder] = StateFor(kind);

        Assert.Equal(ExitCodes.Success, await _h.RunAsync(Url, "--done", "--force"));
        Assert.False(Directory.Exists(folder), kind);
        Assert.DoesNotContain(expected, _h.Error.ToString());
    }

    [Fact]
    public void Blocker_list_is_capped_and_counts_the_rest()
    {
        var review = new Review("/r/owner/repo/pr-12", _pr, OwnershipRecord.Create(_pr, Harness.Now));
        var state = new LocalState(["1", "2", "3", "4", "5", "6", "7"], [], [], 0);
        var text = FinishReview.DescribeBlockers(review, state, _h.Services);
        Assert.Contains("7 changed files: 1, 2, 3, 4, 5, and 2 more", text);
    }

    [Fact]
    public async Task Clean_copy_is_deleted()
    {
        var folder = _h.PlantReview(_pr, lastSyncedHead: "abc1234");

        Assert.Equal(ExitCodes.Success, await _h.RunAsync(Url, "--done"));

        Assert.False(Directory.Exists(folder));
        Assert.Contains("Deleted Owner/Repo#12", _h.Out.ToString());
        Assert.Equal(["abc1234"], _h.Git.KnownCommitsSeen.Single());
    }

    [Fact]
    public async Task Url_is_matched_case_insensitively()
    {
        var folder = _h.PlantReview(_pr);
        Assert.Equal(ExitCodes.Success, await _h.RunAsync("https://github.com/OWNER/repo/pull/12/files", "--done"));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Missing_copy_is_an_error()
    {
        _h.PlantReview(Harness.Pr("Owner", "Repo", 13));
        Assert.Equal(ExitCodes.Failure, await _h.RunAsync(Url, "--done"));
        Assert.Contains("there is no review copy of Owner/Repo#12", _h.Error.ToString());
    }

    [Fact]
    public async Task Missing_root_is_an_error()
    {
        Assert.Equal(ExitCodes.Failure, await _h.RunAsync(Url, "--done"));
        Assert.Contains("there are no review copies", _h.Error.ToString());
    }

    [Fact]
    public async Task Folder_not_created_by_codiff_helper_is_never_deleted_even_with_force()
    {
        var folder = _h.PlantReview(_pr, withRecord: false);

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync(Url, "--done", "--force"));

        Assert.True(Directory.Exists(folder));
        Assert.Contains("was not created by codiff-helper", _h.Error.ToString());
    }

    [Fact]
    public async Task Symlink_escaping_the_root_is_never_deleted_even_with_force()
    {
        var outside = _h.Temp.Combine("outside", "pr-12");
        Directory.CreateDirectory(Path.Combine(outside, ".git"));
        OwnershipRecord.Create(_pr, Harness.Now).WriteTo(outside);
        var link = _h.FolderFor(_pr);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateSymbolicLink(link, outside);

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync(Url, "--done", "--force"));

        Assert.True(Directory.Exists(outside));
        Assert.True(File.Exists(OwnershipRecord.PathIn(outside)));
        Assert.Contains("refusing to delete", _h.Error.ToString());
    }

    [Fact]
    public async Task Symlink_to_another_review_inside_the_root_is_refused()
    {
        var other = _h.PlantReview(Harness.Pr("Owner", "Repo", 13));
        var link = _h.FolderFor(_pr);
        Directory.CreateSymbolicLink(link, other);

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync(Url, "--done", "--force"));

        Assert.True(Directory.Exists(other));
        Assert.Contains("refusing to delete", _h.Error.ToString());
    }

    [Fact]
    public async Task Plain_done_uses_the_review_folder_around_the_current_directory()
    {
        var folder = _h.PlantReview(_pr);
        var deep = Path.Combine(folder, "src", "deep");
        Directory.CreateDirectory(deep);
        _h.CurrentDirectory = deep;

        Assert.Equal(ExitCodes.Success, await _h.RunAsync("--done"));

        Assert.False(Directory.Exists(folder));
        var output = _h.Out.ToString();
        Assert.Contains("Deleted Owner/Repo#12", output);
        Assert.Contains("Your shell is still in the deleted folder; run: cd ~/Reviews", output);
    }

    [Fact]
    public async Task Plain_done_points_at_the_repo_folder_when_it_still_exists()
    {
        var folder = _h.PlantReview(_pr);
        _h.PlantReview(Harness.Pr("Owner", "Repo", 13));
        _h.CurrentDirectory = folder;

        Assert.Equal(ExitCodes.Success, await _h.RunAsync("--done"));
        Assert.Contains("run: cd ~/Reviews/owner/repo", _h.Out.ToString());
    }

    [Fact]
    public async Task Plain_done_still_refuses_local_work()
    {
        var folder = _h.PlantReview(_pr);
        _h.Git.States[folder] = new LocalState(["a"], [], [], 0);
        _h.CurrentDirectory = folder;

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync("--done"));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task Plain_done_outside_a_review_folder_is_an_error()
    {
        _h.PlantReview(_pr);
        _h.CurrentDirectory = _h.Root;

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync("--done"));
        Assert.Contains("not inside a review folder", _h.Error.ToString());
    }

    [Fact]
    public async Task Plain_done_refuses_a_marked_folder_outside_the_root()
    {
        var outside = _h.Temp.Combine("outside", "owner", "repo", "pr-12");
        Directory.CreateDirectory(Path.Combine(outside, ".git"));
        OwnershipRecord.Create(_pr, Harness.Now).WriteTo(outside);
        Directory.CreateDirectory(_h.Root);
        _h.CurrentDirectory = outside;

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync("--done", "--force"));

        Assert.True(Directory.Exists(outside));
        Assert.Contains("is not a review folder inside", _h.Error.ToString());
    }

    [Fact]
    public async Task Unreadable_git_state_refuses_without_force_and_deletes_with_force()
    {
        var folder = _h.PlantReview(_pr);
        _h.Git.Broken.Add(folder);

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync(Url, "--done"));
        Assert.True(Directory.Exists(folder));
        Assert.Contains("use --force to delete it anyway", _h.Error.ToString());

        Assert.Equal(ExitCodes.Success, await _h.RunAsync(Url, "--done", "--force"));
        Assert.False(Directory.Exists(folder));
    }

    public void Dispose() => _h.Dispose();
}
