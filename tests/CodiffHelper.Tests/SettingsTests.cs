namespace CodiffHelper.Tests;

public class SettingsTests
{
    private const string Home = "/Users/someone";

    private static string Resolve(string? configured) =>
        Settings.ResolveRoot(name => name == Settings.RootVariable ? configured : null, Home);

    [Theory]
    [InlineData(null, "/Users/someone/Reviews")]
    [InlineData("", "/Users/someone/Reviews")]
    [InlineData("   ", "/Users/someone/Reviews")]
    [InlineData("/Volumes/work/reviews", "/Volumes/work/reviews")]
    [InlineData("/Volumes/work/reviews/", "/Volumes/work/reviews")]
    [InlineData("/Volumes/work/x/../reviews", "/Volumes/work/reviews")]
    [InlineData("~", "/Users/someone")]
    [InlineData("~/code/reviews", "/Users/someone/code/reviews")]
    public void Resolves_the_reviews_root(string? configured, string expected) => Assert.Equal(expected, Resolve(configured));

    [Theory]
    [InlineData("reviews", "must be an absolute path")]
    [InlineData("~other/reviews", "must be an absolute path")]
    [InlineData("/", "must not be the file system root")]
    public void Rejects_unusable_roots(string configured, string expected)
    {
        var error = Assert.Throws<UserFacingException>(() => Resolve(configured));
        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("/Users/someone/Reviews/a/b/pr-1", "~/Reviews/a/b/pr-1")]
    [InlineData("/Users/someone", "~")]
    [InlineData("/Users/someoneelse/x", "/Users/someoneelse/x")]
    [InlineData("/tmp/x", "/tmp/x")]
    public void Displays_home_as_tilde(string path, string expected) => Assert.Equal(expected, Settings.Display(path, Home));
}
