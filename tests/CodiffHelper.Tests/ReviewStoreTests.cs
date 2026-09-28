using CodiffHelper.Tests.Support;

namespace CodiffHelper.Tests;

public class ReviewStoreTests : IDisposable
{
    private readonly Harness _h = new();
    private readonly PullRequestUrl _pr = Harness.Pr("Owner", "Repo", 12);

    private ReviewStore Store => ReviewStore.OpenOrCreate(_h.Root);

    [Fact]
    public void Folder_is_owner_repo_pr_number_in_lower_case()
    {
        Assert.Equal(Path.Combine(_h.Root, "owner", "repo", "pr-12"), Store.FolderFor(_pr));
    }

    [Fact]
    public void Root_is_resolved_to_its_real_path()
    {
        var realRoot = _h.Temp.Combine("elsewhere");
        Directory.CreateDirectory(realRoot);
        var linkedRoot = _h.Temp.Combine("linked-root");
        Directory.CreateSymbolicLink(linkedRoot, realRoot);

        Assert.Equal(realRoot, ReviewStore.OpenExisting(linkedRoot)!.Root);
        Assert.Null(ReviewStore.OpenExisting(_h.Temp.Combine("missing")));
    }

    [Fact]
    public void Loads_a_planted_review()
    {
        var folder = _h.PlantReview(_pr);
        var review = Store.TryLoad(folder, out _);
        Assert.NotNull(review);
        Assert.Equal(folder, review.Path);
        Assert.Equal(_pr, review.PullRequest);
    }

    [Fact]
    public void Folder_without_record_is_not_a_review()
    {
        var folder = _h.PlantReview(_pr, withRecord: false);
        Assert.Null(Store.TryLoad(folder, out var reason));
        Assert.Contains("was not created by codiff-helper", reason);
    }

    [Fact]
    public void Record_that_does_not_match_the_location_is_refused()
    {
        var folder = _h.PlantReview(_pr);
        var other = Path.Combine(_h.Root, "owner", "repo", "pr-13");
        Directory.Move(folder, other);

        Assert.Null(Store.TryLoad(other, out var reason));
        Assert.Contains("does not match its location", reason);
    }

    [Theory]
    [InlineData("owner/repo")]
    [InlineData("owner/repo/pr-12/nested")]
    [InlineData("owner/repo/review-12")]
    [InlineData("owner/repo/pr-012")]
    public void Only_folders_exactly_at_owner_repo_pr_n_count(string relative)
    {
        var folder = Path.Combine([_h.Root, .. relative.Split('/')]);
        Directory.CreateDirectory(Path.Combine(folder, ".git"));
        OwnershipRecord.Create(_pr, Harness.Now).WriteTo(folder);

        Assert.Null(Store.TryLoad(folder, out var reason));
        Assert.Contains("is not a review folder inside", reason);
    }

    [Fact]
    public void Root_itself_is_never_a_review()
    {
        Directory.CreateDirectory(Path.Combine(_h.Root, ".git"));
        OwnershipRecord.Create(_pr, Harness.Now).WriteTo(_h.Root);
        Assert.Null(Store.TryLoad(_h.Root, out _));
    }

    [Fact]
    public void Folder_outside_the_root_is_refused_even_with_a_record()
    {
        var outside = _h.Temp.Combine("outside", "owner", "repo", "pr-12");
        Directory.CreateDirectory(Path.Combine(outside, ".git"));
        OwnershipRecord.Create(_pr, Harness.Now).WriteTo(outside);

        Assert.Null(Store.TryLoad(outside, out var reason));
        Assert.Contains("is not a review folder inside", reason);
    }

    [Fact]
    public void Dot_dot_paths_that_escape_the_root_are_refused()
    {
        var outside = _h.Temp.Combine("outside", "owner", "repo", "pr-12");
        Directory.CreateDirectory(Path.Combine(outside, ".git"));
        OwnershipRecord.Create(_pr, Harness.Now).WriteTo(outside);
        Directory.CreateDirectory(Path.Combine(_h.Root, "owner", "repo"));

        var sneaky = Path.Combine(_h.Root, "owner", "repo", "..", "..", "..", "..", "outside", "owner", "repo", "pr-12");
        Assert.Null(Store.TryLoad(sneaky, out _));
    }

    [Fact]
    public void Symlink_that_escapes_the_root_is_refused()
    {
        var outside = _h.Temp.Combine("outside", "pr-12");
        Directory.CreateDirectory(Path.Combine(outside, ".git"));
        OwnershipRecord.Create(_pr, Harness.Now).WriteTo(outside);

        var link = Store.FolderFor(_pr);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateSymbolicLink(link, outside);

        Assert.Null(Store.TryLoad(link, out var reason));
        Assert.Contains("is not a review folder inside", reason);
    }

    [Fact]
    public void Symlinked_owner_folder_that_escapes_the_root_is_refused()
    {
        var outsideOwner = _h.Temp.Combine("outside-owner");
        var outsideReview = Path.Combine(outsideOwner, "repo", "pr-12");
        Directory.CreateDirectory(Path.Combine(outsideReview, ".git"));
        OwnershipRecord.Create(_pr, Harness.Now).WriteTo(outsideReview);
        Directory.CreateDirectory(_h.Root);
        Directory.CreateSymbolicLink(Path.Combine(_h.Root, "owner"), outsideOwner);

        Assert.Null(Store.TryLoad(Store.FolderFor(_pr), out _));
        Assert.Empty(Store.List());
    }

    [Fact]
    public void List_returns_only_valid_reviews_sorted()
    {
        _h.PlantReview(Harness.Pr("b", "z", 2));
        _h.PlantReview(Harness.Pr("a", "y", 10));
        _h.PlantReview(Harness.Pr("a", "y", 9));
        _h.PlantReview(Harness.Pr("c", "x", 1), withRecord: false);
        Directory.CreateDirectory(Path.Combine(_h.Root, "c", "x", ".pr-3.partial-abc", ".git"));
        File.WriteAllText(Path.Combine(_h.Root, "stray-file"), "");

        var names = Store.List().Select(r => r.PullRequest.DisplayName);
        Assert.Equal(["a/y#9", "a/y#10", "b/z#2"], names);
    }

    [Fact]
    public void List_skips_symlinked_review_folders()
    {
        var target = _h.PlantReview(_pr);
        var alias = Path.Combine(_h.Root, "owner", "repo", "pr-99");
        Directory.CreateSymbolicLink(alias, target);

        Assert.Equal([target], Store.List().Select(r => r.Path));
    }

    [Fact]
    public void Delete_removes_the_folder_and_empty_parents_but_never_the_root()
    {
        var folder = _h.PlantReview(_pr);
        var store = Store;
        store.Delete(store.TryLoad(folder, out _)!);

        Assert.False(Directory.Exists(folder));
        Assert.False(Directory.Exists(Path.Combine(_h.Root, "owner")));
        Assert.True(Directory.Exists(_h.Root));
    }

    [Fact]
    public void Delete_keeps_parents_that_hold_other_reviews()
    {
        var first = _h.PlantReview(_pr);
        var second = _h.PlantReview(Harness.Pr("Owner", "Repo", 13));
        var store = Store;
        store.Delete(store.TryLoad(first, out _)!);

        Assert.False(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public void Delete_does_not_follow_symlinks_inside_the_review()
    {
        var precious = _h.Temp.Combine("precious");
        Directory.CreateDirectory(precious);
        File.WriteAllText(Path.Combine(precious, "keep.txt"), "keep");
        var preciousFile = _h.Temp.Combine("precious-file.txt");
        File.WriteAllText(preciousFile, "keep");

        var folder = _h.PlantReview(_pr);
        Directory.CreateSymbolicLink(Path.Combine(folder, "linked-dir"), precious);
        File.CreateSymbolicLink(Path.Combine(folder, "linked-file"), preciousFile);

        var store = Store;
        store.Delete(store.TryLoad(folder, out _)!);

        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(precious, "keep.txt")));
        Assert.True(File.Exists(preciousFile));
    }

    [Fact]
    public void Delete_rechecks_the_record_just_before_deleting()
    {
        var folder = _h.PlantReview(_pr);
        var store = Store;
        var review = store.TryLoad(folder, out _)!;
        File.Delete(OwnershipRecord.PathIn(folder));

        Assert.Throws<UserFacingException>(() => store.Delete(review));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Finds_the_enclosing_review_folder_by_walking_up()
    {
        var folder = _h.PlantReview(_pr);
        var deep = Path.Combine(folder, "src", "lib");
        Directory.CreateDirectory(deep);

        Assert.Equal(folder, ReviewStore.FindEnclosingRecordFolder(deep));
        Assert.Equal(folder, ReviewStore.FindEnclosingRecordFolder(folder));
        Assert.Null(ReviewStore.FindEnclosingRecordFolder(_h.Home));
    }

    [Fact]
    public void Real_path_resolves_links_and_dots()
    {
        var target = _h.Temp.Combine("target");
        Directory.CreateDirectory(target);
        var link = _h.Temp.Combine("link");
        Directory.CreateSymbolicLink(link, target);

        Assert.Equal(target, ReviewStore.RealPath(link));
        Assert.Equal(target, ReviewStore.RealPath(Path.Combine(link, "..", "target", ".")));
        Assert.Null(ReviewStore.RealPath(_h.Temp.Combine("nope")));
    }

    public void Dispose() => _h.Dispose();
}
