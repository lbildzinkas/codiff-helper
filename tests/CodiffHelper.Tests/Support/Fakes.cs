namespace CodiffHelper.Tests.Support;

internal sealed class FakeGit : IGit
{
    public Dictionary<string, LocalState> States { get; } = new();
    public Dictionary<string, string> Heads { get; } = new();
    public HashSet<string> Broken { get; } = new();
    public List<IReadOnlyCollection<string>> KnownCommitsSeen { get; } = new();

    public Task<LocalState> GetLocalStateAsync(
        string repositoryPath, IReadOnlyCollection<string> knownRemoteCommits, CancellationToken cancellationToken = default)
    {
        KnownCommitsSeen.Add(knownRemoteCommits);
        if (Broken.Contains(repositoryPath)) throw new UserFacingException($"git status failed in {repositoryPath}: broken");
        return Task.FromResult(States.GetValueOrDefault(repositoryPath, LocalState.Clean));
    }

    public Task<string?> GetHeadCommitAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Heads.TryGetValue(repositoryPath, out var head) ? head : null);
}

/// <summary>Fake GitHub: cloning makes a folder with an empty .git directory; checkout moves a fake HEAD.</summary>
internal sealed class FakeGitHub(FakeGit git) : IGitHub
{
    public Dictionary<int, PullRequestInfo> PullRequests { get; } = new();
    public string? NotReadyMessage { get; set; }
    public string? CloneFailure { get; set; }
    public string? CheckoutFailure { get; set; }
    public HashSet<int> LookupFailures { get; } = new();
    public List<string> Clones { get; } = new();
    public List<string> Checkouts { get; } = new();

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default) =>
        NotReadyMessage is null ? Task.CompletedTask : throw new UserFacingException(NotReadyMessage);

    public Task<PullRequestInfo> GetPullRequestAsync(PullRequestUrl pullRequest, CancellationToken cancellationToken = default)
    {
        if (LookupFailures.Contains(pullRequest.Number) || !PullRequests.TryGetValue(pullRequest.Number, out var info))
            throw new UserFacingException($"could not load {pullRequest.DisplayName} from GitHub: not found");
        return Task.FromResult(info);
    }

    public Task CloneAsync(PullRequestUrl pullRequest, string destination, CancellationToken cancellationToken = default)
    {
        Clones.Add(destination);
        if (CloneFailure is not null)
        {
            Directory.CreateDirectory(Path.Combine(destination, ".git"));
            throw new UserFacingException(CloneFailure);
        }
        Directory.CreateDirectory(Path.Combine(destination, ".git"));
        return Task.CompletedTask;
    }

    public Task CheckoutAsync(string repositoryPath, PullRequestUrl pullRequest, CancellationToken cancellationToken = default)
    {
        Checkouts.Add(repositoryPath);
        if (CheckoutFailure is not null) throw new UserFacingException(CheckoutFailure);
        git.Heads[repositoryPath] = PullRequests[pullRequest.Number].HeadCommit;
        return Task.CompletedTask;
    }
}

internal sealed class FakeCodiff : ICodiff
{
    public bool Installed { get; set; } = true;
    public string? LaunchFailure { get; set; }
    public List<CodiffLaunch> Launches { get; } = new();

    public void EnsureInstalled()
    {
        if (!Installed) throw new UserFacingException("the codiff command was not found; install Codiff");
    }

    public Task LaunchAsync(CodiffLaunch launch, CancellationToken cancellationToken = default)
    {
        if (LaunchFailure is not null) throw new UserFacingException(LaunchFailure);
        Launches.Add(launch);
        return Task.CompletedTask;
    }
}

/// <summary>Records requests and answers them from a script of canned results.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<(Func<ProcessRequest, bool> Match, Func<ProcessRequest, ProcessResult> Answer)> _rules = new();

    public List<ProcessRequest> Requests { get; } = new();
    public HashSet<string> MissingCommands { get; } = new();
    public int? LaunchExitCode { get; set; }

    public FakeProcessRunner When(Func<ProcessRequest, bool> match, ProcessResult result)
    {
        _rules.Add((match, _ => result));
        return this;
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (MissingCommands.Contains(request.FileName)) throw new CommandNotFoundException(request.FileName);
        foreach (var (match, answer) in _rules)
        {
            if (match(request)) return Task.FromResult(answer(request));
        }
        return Task.FromResult(new ProcessResult(0, "", ""));
    }

    public Task<int?> LaunchAsync(ProcessRequest request, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (MissingCommands.Contains(request.FileName)) throw new CommandNotFoundException(request.FileName);
        return Task.FromResult(LaunchExitCode);
    }
}

/// <summary>A fixed clock for predictable ownership records.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
