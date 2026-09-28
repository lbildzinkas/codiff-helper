using CodiffHelper.Tests.Support;

namespace CodiffHelper.Tests;

public class OwnershipRecordTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly PullRequestUrl _pr = new("Owner", "Repo", 5);

    private string NewFolder()
    {
        var folder = _temp.Combine($"f-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(folder, ".git"));
        return folder;
    }

    [Fact]
    public void Round_trips_inside_the_git_directory()
    {
        var folder = NewFolder();
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        (OwnershipRecord.Create(_pr, now) with { LastSyncedHead = "abc1234", Title = "T", State = "OPEN" }).WriteTo(folder);

        Assert.True(File.Exists(Path.Combine(folder, ".git", "codiff-helper.json")));
        Assert.Single(Directory.EnumerateFileSystemEntries(folder)); // nothing but .git in the working tree

        var record = OwnershipRecord.TryRead(folder, out var pullRequest);
        Assert.NotNull(record);
        Assert.Equal(_pr, pullRequest);
        Assert.Equal("https://github.com/Owner/Repo/pull/5", record.Url);
        Assert.Equal(now, record.CreatedAt);
        Assert.Equal("abc1234", record.LastSyncedHead);
        Assert.Equal("T", record.Title);
    }

    [Theory]
    [InlineData("""{"formatVersion":1,"url":"https://github.com/a/b/pull/1","createdAt":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"tool":"other","formatVersion":1,"url":"https://github.com/a/b/pull/1","createdAt":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"tool":"codiff-helper","formatVersion":2,"url":"https://github.com/a/b/pull/1","createdAt":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"tool":"codiff-helper","formatVersion":1,"createdAt":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"tool":"codiff-helper","formatVersion":1,"url":"https://gitlab.com/a/b/pull/1","createdAt":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"tool":"codiff-helper","formatVersion":1,"url":"https://github.com/a/b/pull/1"}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Invalid_records_are_ignored(string content)
    {
        var folder = NewFolder();
        File.WriteAllText(OwnershipRecord.PathIn(folder), content);
        Assert.Null(OwnershipRecord.TryRead(folder, out _));
    }

    [Fact]
    public void Missing_record_or_git_directory_is_not_valid()
    {
        var folder = NewFolder();
        Assert.Null(OwnershipRecord.TryRead(folder, out _));
        Assert.Null(OwnershipRecord.TryRead(_temp.Combine("does-not-exist"), out _));
    }

    [Fact]
    public void Symlinked_git_directory_or_record_is_not_trusted()
    {
        var real = NewFolder();
        OwnershipRecord.Create(_pr, DateTimeOffset.UtcNow).WriteTo(real);

        var linkedGit = _temp.Combine("linked-git");
        Directory.CreateDirectory(linkedGit);
        Directory.CreateSymbolicLink(Path.Combine(linkedGit, ".git"), Path.Combine(real, ".git"));
        Assert.Null(OwnershipRecord.TryRead(linkedGit, out _));

        var linkedRecord = NewFolder();
        File.CreateSymbolicLink(OwnershipRecord.PathIn(linkedRecord), OwnershipRecord.PathIn(real));
        Assert.Null(OwnershipRecord.TryRead(linkedRecord, out _));
    }

    [Fact]
    public void Writing_needs_a_git_directory()
    {
        var folder = _temp.Combine("plain");
        Directory.CreateDirectory(folder);
        Assert.Throws<UserFacingException>(() => OwnershipRecord.Create(_pr, DateTimeOffset.UtcNow).WriteTo(folder));
    }

    public void Dispose() => _temp.Dispose();
}
