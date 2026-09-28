namespace CodiffHelper.Tests.Support;

/// <summary>
/// An isolated world for one test: a temp home, a reviews root inside it, fakes for gh, git and codiff, and
/// captured output. Nothing here reads the real environment or touches the real ~/Reviews.
/// </summary>
internal sealed class Harness : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    public Harness()
    {
        Home = Temp.Combine("home");
        Root = Temp.Combine("home", "Reviews");
        Directory.CreateDirectory(Home);
        Environment[Settings.RootVariable] = Root;
        CurrentDirectory = Home;
        GitHub = new FakeGitHub(Git);
    }

    public TempDirectory Temp { get; } = new();
    public string Home { get; }
    public string Root { get; }
    public string CurrentDirectory { get; set; }
    public Dictionary<string, string?> Environment { get; } = new();
    public FakeGit Git { get; } = new();
    public FakeGitHub GitHub { get; }
    public FakeCodiff Codiff { get; } = new();
    public IGit? GitOverride { get; set; }
    public IGitHub? GitHubOverride { get; set; }
    public StringWriter Out { get; } = new();
    public StringWriter Error { get; } = new();

    public AppServices Services => new(
        Out,
        Error,
        name => Environment.GetValueOrDefault(name),
        Home,
        CurrentDirectory,
        GitOverride ?? Git,
        GitHubOverride ?? GitHub,
        Codiff,
        new FixedTimeProvider(Now));

    public Task<int> RunAsync(params string[] args) => App.RunAsync(args, Services);

    public static PullRequestUrl Pr(string owner, string repo, int number) => new(owner, repo, number);

    /// <summary>The folder where codiff-helper keeps a pull request's copy under this harness's root.</summary>
    public string FolderFor(PullRequestUrl pullRequest) =>
        Path.Combine(Root, pullRequest.Owner.ToLowerInvariant(), pullRequest.Repo.ToLowerInvariant(), $"pr-{pullRequest.Number}");

    /// <summary>Creates a review folder by hand, with a valid ownership record unless told otherwise.</summary>
    public string PlantReview(PullRequestUrl pullRequest, bool withRecord = true, string? lastSyncedHead = null)
    {
        var folder = FolderFor(pullRequest);
        Directory.CreateDirectory(Path.Combine(folder, ".git"));
        File.WriteAllText(Path.Combine(folder, "README.md"), "content");
        if (withRecord)
        {
            (OwnershipRecord.Create(pullRequest, Now) with { Title = "Planted", State = "OPEN", LastSyncedHead = lastSyncedHead })
                .WriteTo(folder);
        }
        return folder;
    }

    public void Dispose() => Temp.Dispose();
}
