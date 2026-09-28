namespace CodiffHelper.Tests.Support;

/// <summary>Runs real git with the user's and system configuration switched off, for local temp repositories.</summary>
internal sealed class IsolatedGitRunner : IProcessRunner
{
    private static readonly Dictionary<string, string?> IsolatedEnvironment = new()
    {
        ["GIT_CONFIG_GLOBAL"] = "/dev/null",
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_AUTHOR_NAME"] = "Test",
        ["GIT_AUTHOR_EMAIL"] = "test@example.com",
        ["GIT_COMMITTER_NAME"] = "Test",
        ["GIT_COMMITTER_EMAIL"] = "test@example.com",
        ["GIT_TERMINAL_PROMPT"] = "0",
    };

    private readonly ProcessRunner _inner = new();

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default) =>
        _inner.RunAsync(Isolate(request), cancellationToken);

    public Task<int?> LaunchAsync(ProcessRequest request, TimeSpan wait, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("tests never launch programs");

    private static ProcessRequest Isolate(ProcessRequest request)
    {
        var environment = new Dictionary<string, string?>(IsolatedEnvironment);
        foreach (var (key, value) in request.Environment ?? new Dictionary<string, string?>()) environment[key] = value;
        return request with { Environment = environment };
    }
}

internal static class GitRepo
{
    private static readonly IsolatedGitRunner Runner = new();

    public static string Run(string workingDirectory, params string[] arguments)
    {
        var result = Runner.RunAsync(new ProcessRequest("git", arguments, workingDirectory)).GetAwaiter().GetResult();
        if (!result.Succeeded)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.StandardError}");
        return result.StandardOutput.Trim();
    }

    /// <summary>Creates a bare "GitHub" repository with one commit on main, and returns its path.</summary>
    public static string CreateRemote(string parent)
    {
        var seed = Path.Combine(parent, "seed");
        var remote = Path.Combine(parent, "remote.git");
        Directory.CreateDirectory(seed);
        Run(seed, "init", "--quiet", "--initial-branch=main");
        File.WriteAllText(Path.Combine(seed, "README.md"), "hello\n");
        Run(seed, "add", ".");
        Run(seed, "commit", "--quiet", "-m", "initial");
        Run(parent, "clone", "--quiet", "--bare", seed, remote);
        return remote;
    }

    /// <summary>Pushes a new commit to <c>refs/pull/&lt;number&gt;/head</c> on the remote; returns its id.</summary>
    public static string PushPullRequestCommit(string parent, string remote, int number, string fileContent)
    {
        var work = Path.Combine(parent, $"work-{Guid.NewGuid():N}");
        Run(parent, "clone", "--quiet", remote, work);
        var reference = $"refs/pull/{number}/head";
        var fetched = Runner.RunAsync(new ProcessRequest("git", ["fetch", "--quiet", "origin", reference], work))
            .GetAwaiter().GetResult();
        if (fetched.Succeeded) Run(work, "checkout", "--quiet", "FETCH_HEAD");
        File.WriteAllText(Path.Combine(work, "change.txt"), fileContent);
        Run(work, "add", ".");
        Run(work, "commit", "--quiet", "-m", $"pull request change {fileContent}");
        Run(work, "push", "--quiet", "--force", "origin", $"HEAD:{reference}");
        return Run(work, "rev-parse", "HEAD");
    }
}

/// <summary>
/// A GitHub stand-in backed by a local bare repository: cloning and checking out use real git, so the update and
/// safety logic is exercised against real repositories.
/// </summary>
internal sealed class LocalGitHub(string remote) : IGitHub
{
    public Dictionary<int, PullRequestInfo> PullRequests { get; } = new();
    public int Checkouts { get; private set; }

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<PullRequestInfo> GetPullRequestAsync(PullRequestUrl pullRequest, CancellationToken cancellationToken = default) =>
        Task.FromResult(PullRequests.GetValueOrDefault(pullRequest.Number) ?? new PullRequestInfo("Local PR", "OPEN"));

    public Task CloneAsync(PullRequestUrl pullRequest, string destination, CancellationToken cancellationToken = default)
    {
        GitRepo.Run(Path.GetDirectoryName(destination)!, "clone", "--quiet", remote, destination);
        return Task.CompletedTask;
    }

    public Task CheckoutAsync(string repositoryPath, PullRequestUrl pullRequest, CancellationToken cancellationToken = default)
    {
        Checkouts++;
        GitRepo.Run(repositoryPath, "fetch", "--quiet", "origin", $"refs/pull/{pullRequest.Number}/head");
        GitRepo.Run(repositoryPath, "checkout", "--quiet", "-B", GhCli.BranchName(pullRequest), "FETCH_HEAD");
        return Task.CompletedTask;
    }
}
