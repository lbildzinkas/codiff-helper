namespace CodiffHelper.Tests;

public class PullRequestUrlTests
{
    [Theory]
    [InlineData("https://github.com/nkzw-tech/codiff/pull/42", "nkzw-tech", "codiff", 42)]
    [InlineData("https://github.com/nkzw-tech/codiff/pull/42/", "nkzw-tech", "codiff", 42)]
    [InlineData("https://github.com/nkzw-tech/codiff/pull/42/files", "nkzw-tech", "codiff", 42)]
    [InlineData("https://github.com/nkzw-tech/codiff/pull/42/commits/abc123", "nkzw-tech", "codiff", 42)]
    [InlineData("https://github.com/nkzw-tech/codiff/pull/42?diff=split", "nkzw-tech", "codiff", 42)]
    [InlineData("https://github.com/nkzw-tech/codiff/pull/42#discussion_r1", "nkzw-tech", "codiff", 42)]
    [InlineData("https://github.com/nkzw-tech/codiff/pull/42/files?w=1#diff-abc", "nkzw-tech", "codiff", 42)]
    [InlineData("  https://github.com/Owner/Repo.Name_x-y/pull/7  ", "Owner", "Repo.Name_x-y", 7)]
    [InlineData("https://www.github.com/a/b/pull/1", "a", "b", 1)]
    [InlineData("http://github.com/a/b/pull/1", "a", "b", 1)]
    [InlineData("https://GitHub.com/a/b/pull/1", "a", "b", 1)]
    public void Accepts_pull_request_urls(string text, string owner, string repo, int number)
    {
        Assert.True(PullRequestUrl.TryParse(text, out var pullRequest, out var error), error);
        Assert.Equal(new PullRequestUrl(owner, repo, number), pullRequest);
    }

    [Fact]
    public void Canonical_url_drops_trailing_segments_query_and_fragment()
    {
        Assert.True(PullRequestUrl.TryParse("https://github.com/a/b/pull/12/files?x=1#y", out var pullRequest, out _));
        Assert.Equal("https://github.com/a/b/pull/12", pullRequest.Url);
        Assert.Equal("a/b", pullRequest.RepoSlug);
        Assert.Equal("a/b#12", pullRequest.DisplayName);
    }

    [Theory]
    [InlineData("", "expected a pull request URL")]
    [InlineData("github.com/a/b/pull/1", "is not a URL")]
    [InlineData("a/b#1", "is not a URL")]
    [InlineData("ftp://github.com/a/b/pull/1", "is not a URL")]
    [InlineData("https://gitlab.com/a/b/pull/1", "only GitHub")]
    [InlineData("https://github.com.evil.example/a/b/pull/1", "only GitHub")]
    [InlineData("https://github.com:8443/a/b/pull/1", "is not a pull request URL")]
    [InlineData("https://user@github.com/a/b/pull/1", "is not a pull request URL")]
    [InlineData("https://github.com/a/b", "is not a pull request URL")]
    [InlineData("https://github.com/a/b/issues/1", "is not a pull request URL")]
    [InlineData("https://github.com/a/b/pulls", "is not a pull request URL")]
    [InlineData("https://github.com/a/b/pull/", "is not a pull request URL")]
    [InlineData("https://github.com/a/b/pull/abc", "valid pull request number")]
    [InlineData("https://github.com/a/b/pull/0", "valid pull request number")]
    [InlineData("https://github.com/a/b/pull/-3", "valid pull request number")]
    [InlineData("https://github.com/a/b/pull/99999999999", "valid pull request number")]
    [InlineData("https://github.com/-a/b/pull/1", "valid GitHub owner")]
    [InlineData("https://github.com/a%2F../b/pull/1", "valid GitHub owner")]
    [InlineData("https://github.com/a/b%20c/pull/1", "valid GitHub owner")]
    public void Rejects_anything_else_with_a_clear_message(string text, string expected)
    {
        Assert.False(PullRequestUrl.TryParse(text, out var pullRequest, out var error));
        Assert.Null(pullRequest);
        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("...", true)]
    [InlineData(".github", true)]
    [InlineData("a/b", false)]
    public void Repository_names_never_allow_path_tricks(string repo, bool valid) =>
        Assert.Equal(valid, PullRequestUrl.IsValidRepo(repo));
}
