using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodiffHelper;

internal sealed record PullRequestInfo(string Title, string State)
{
    public bool IsOpen => State.Equals("OPEN", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Seam over everything that needs GitHub access; the real implementation uses gh.</summary>
internal interface IGitHub
{
    /// <summary>Fails with a message naming the fix when gh is missing or not signed in.</summary>
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);

    Task<PullRequestInfo> GetPullRequestAsync(PullRequestUrl pullRequest, CancellationToken cancellationToken = default);

    /// <summary>Makes a blobless partial clone of the pull request's repository at <paramref name="destination"/>.</summary>
    Task CloneAsync(PullRequestUrl pullRequest, string destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks out the pull request's latest commits on the local branch <see cref="GhCli.BranchName"/>, resetting
    /// that branch when it already exists. Callers must only do this when the copy has no local work.
    /// </summary>
    Task CheckoutAsync(string repositoryPath, PullRequestUrl pullRequest, CancellationToken cancellationToken = default);
}

internal sealed class GhCli(IProcessRunner runner) : IGitHub
{
    public static string BranchName(PullRequestUrl pullRequest) => $"pr-{pullRequest.Number}";

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var status = await RunGhAsync(["auth", "status", "--hostname", "github.com"], null, cancellationToken).ConfigureAwait(false);
        if (!status.Succeeded)
            throw new UserFacingException("gh is not signed in to GitHub; run: gh auth login");
    }

    public async Task<PullRequestInfo> GetPullRequestAsync(PullRequestUrl pullRequest, CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync(
            ["pr", "view", pullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
             "--repo", pullRequest.RepoSlug, "--json", "title,state"],
            null,
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new UserFacingException($"could not load {pullRequest.DisplayName} from GitHub: {result.ErrorSummary}");

        return ParsePullRequest(result.StandardOutput)
               ?? throw new UserFacingException($"could not read gh's answer about {pullRequest.DisplayName}");
    }

    public async Task CloneAsync(PullRequestUrl pullRequest, string destination, CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync(
            ["repo", "clone", pullRequest.RepoSlug, destination, "--no-upstream", "--", "--filter=blob:none", "--quiet"],
            null,
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new UserFacingException($"could not clone {pullRequest.RepoSlug}: {result.ErrorSummary}");
    }

    public async Task CheckoutAsync(string repositoryPath, PullRequestUrl pullRequest, CancellationToken cancellationToken = default)
    {
        var number = pullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var branch = BranchName(pullRequest);
        var checkout = await RunGhAsync(
            ["pr", "checkout", number, "--repo", pullRequest.RepoSlug, "--branch", branch, "--force"],
            repositoryPath,
            cancellationToken).ConfigureAwait(false);
        if (checkout.Succeeded) return;

        // gh fetches a same-repository pull request through its head branch, which is often deleted once the
        // pull request is merged or closed. GitHub keeps refs/pull/<number>/head, so fetch that instead, still
        // authenticating through gh.
        var fetch = await RunGitAsync(
            repositoryPath,
            ["-c", "credential.helper=", "-c", "credential.helper=!gh auth git-credential",
             "fetch", "--quiet", "origin", $"refs/pull/{number}/head"],
            cancellationToken).ConfigureAwait(false);
        if (!fetch.Succeeded)
            throw new UserFacingException($"could not check out {pullRequest.DisplayName}: {checkout.ErrorSummary}");

        var reset = await RunGitAsync(repositoryPath, ["checkout", "--quiet", "-B", branch, "FETCH_HEAD"], cancellationToken)
            .ConfigureAwait(false);
        if (!reset.Succeeded)
            throw new UserFacingException($"could not check out {pullRequest.DisplayName}: {reset.ErrorSummary}");
    }

    internal static PullRequestInfo? ParsePullRequest(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize(json, GhJsonContext.Default.GhPullRequest);
            if (parsed?.Title is null || parsed.State is null) return null;
            return new PullRequestInfo(parsed.Title, parsed.State);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ProcessResult> RunGhAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string?>
        {
            ["GH_PROMPT_DISABLED"] = "1",
            ["GH_NO_UPDATE_NOTIFIER"] = "1",
            ["NO_COLOR"] = "1",
        };
        try
        {
            return await runner.RunAsync(new ProcessRequest("gh", arguments, workingDirectory, environment), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CommandNotFoundException)
        {
            throw new UserFacingException("the GitHub CLI (gh) is not installed; install it with: brew install gh, then run: gh auth login");
        }
    }

    private Task<ProcessResult> RunGitAsync(string repositoryPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        runner.RunAsync(new ProcessRequest("git", arguments, repositoryPath), cancellationToken);
}

internal sealed record GhPullRequest(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("state")] string? State);

[JsonSerializable(typeof(GhPullRequest))]
internal sealed partial class GhJsonContext : JsonSerializerContext;
