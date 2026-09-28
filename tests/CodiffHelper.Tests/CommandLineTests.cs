namespace CodiffHelper.Tests;

public class CommandLineTests
{
    private const string Url = "https://github.com/a/b/pull/3";
    private static readonly PullRequestUrl Pr = new("a", "b", 3);

    [Fact]
    public void Url_alone_opens_with_the_walkthrough() =>
        Assert.Equal(new OpenCommand(Pr, Walkthrough: true, Agent: null), CommandLine.Parse([Url]));

    [Fact]
    public void No_walkthrough_and_agent_are_passed_on()
    {
        Assert.Equal(new OpenCommand(Pr, false, "pi"), CommandLine.Parse([Url, "--no-walkthrough", "--agent", "pi"]));
        Assert.Equal(new OpenCommand(Pr, true, "claude"), CommandLine.Parse(["--agent=claude", Url]));
    }

    [Fact]
    public void Done_with_and_without_url()
    {
        Assert.Equal(new DoneCommand(Pr, Force: false), CommandLine.Parse([Url, "--done"]));
        Assert.Equal(new DoneCommand(Pr, Force: true), CommandLine.Parse(["--done", "--force", Url]));
        Assert.Equal(new DoneCommand(null, Force: false), CommandLine.Parse(["--done"]));
        Assert.Equal(new DoneCommand(null, Force: true), CommandLine.Parse(["--done", "--force"]));
    }

    [Fact]
    public void List_help_and_version()
    {
        Assert.IsType<ListCommand>(CommandLine.Parse(["--list"]));
        Assert.IsType<HelpCommand>(CommandLine.Parse(["--help"]));
        Assert.IsType<HelpCommand>(CommandLine.Parse([Url, "-h"]));
        Assert.IsType<VersionCommand>(CommandLine.Parse(["--version"]));
        Assert.IsType<VersionCommand>(CommandLine.Parse(["-v"]));
    }

    [Theory]
    [InlineData(new string[0], "missing pull request URL")]
    [InlineData(new[] { "--bogus" }, "unknown option '--bogus'")]
    [InlineData(new[] { Url, Url }, "expected one pull request URL")]
    [InlineData(new[] { Url, "--force" }, "--force only works together with --done")]
    [InlineData(new[] { "--list", Url }, "--list takes no URL")]
    [InlineData(new[] { "--list", "--done" }, "--list takes no URL")]
    [InlineData(new[] { Url, "--done", "--agent", "pi" }, "--done cannot be combined")]
    [InlineData(new[] { Url, "--done", "--no-walkthrough" }, "--done cannot be combined")]
    [InlineData(new[] { Url, "--agent" }, "--agent needs a name")]
    [InlineData(new[] { Url, "--agent", "--done" }, "is not an agent name")]
    [InlineData(new[] { Url, "--agent=" }, "is not an agent name")]
    [InlineData(new[] { "https://github.com/a/b/issues/3" }, "is not a pull request URL")]
    public void Usage_errors_exit_with_code_2(string[] args, string expected)
    {
        var error = Assert.Throws<UserFacingException>(() => CommandLine.Parse(args));
        Assert.Equal(ExitCodes.Usage, error.ExitCode);
        Assert.Contains(expected, error.Message);
        Assert.Contains("--help", error.Message);
    }
}
