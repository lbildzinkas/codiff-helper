using System.Diagnostics;
using CodiffHelper.Tests.Support;

namespace CodiffHelper.Tests;

public class AppTests : IDisposable
{
    private readonly Harness _h = new();

    [Fact]
    public async Task Help_prints_usage_and_succeeds()
    {
        Assert.Equal(ExitCodes.Success, await _h.RunAsync("--help"));
        var output = _h.Out.ToString();
        Assert.Contains("codiff-helper <pull-request-url> --done [--force]", output);
        Assert.Contains("CODIFF_HELPER_ROOT", output);
    }

    [Fact]
    public async Task Version_prints_the_version()
    {
        Assert.Equal(ExitCodes.Success, await _h.RunAsync("--version"));
        Assert.Matches(@"^codiff-helper \d+\.\d+\.\d+", _h.Out.ToString());
    }

    [Fact]
    public async Task Usage_errors_are_one_line_with_exit_code_2()
    {
        Assert.Equal(ExitCodes.Usage, await _h.RunAsync("https://github.com/a/b/issues/1"));
        var error = _h.Error.ToString();
        Assert.StartsWith("codiff-helper: ", error);
        Assert.Single(error.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Missing_git_names_the_fix()
    {
        _h.GitOverride = new GitCli(new MissingCommandRunner());
        _h.PlantReview(Harness.Pr("a", "b", 1));

        Assert.Equal(ExitCodes.Failure, await _h.RunAsync("https://github.com/a/b/pull/1", "--done"));
        Assert.Equal("codiff-helper: git is not installed or not on PATH; install it with: xcode-select --install", _h.Error.ToString().Trim());
    }

    [Fact]
    public async Task Relative_root_setting_is_reported()
    {
        _h.Environment[Settings.RootVariable] = "reviews";
        Assert.Equal(ExitCodes.Failure, await _h.RunAsync("--list"));
        Assert.Contains("CODIFF_HELPER_ROOT must be an absolute path", _h.Error.ToString());
    }

    [Fact]
    public async Task Cancelling_stops_a_running_command()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProcessRunner().RunAsync(new ProcessRequest("/bin/sleep", ["30"]), cancellation.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    public void Dispose() => _h.Dispose();

    private sealed class MissingCommandRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default) =>
            throw new CommandNotFoundException(request.FileName);

        public Task<int?> LaunchAsync(ProcessRequest request, TimeSpan wait, CancellationToken cancellationToken = default) =>
            throw new CommandNotFoundException(request.FileName);
    }
}
