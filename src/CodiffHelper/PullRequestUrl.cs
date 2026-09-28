using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CodiffHelper;

/// <summary>A GitHub pull request identified by owner, repository and number.</summary>
internal sealed partial record PullRequestUrl(string Owner, string Repo, int Number)
{
    public const string ExpectedShape = "https://github.com/<owner>/<repo>/pull/<number>";

    /// <summary>The canonical URL, without trailing segments, query or fragment.</summary>
    public string Url => $"https://github.com/{Owner}/{Repo}/pull/{Number}";

    /// <summary>The <c>owner/repo</c> form accepted by <c>gh --repo</c>.</summary>
    public string RepoSlug => $"{Owner}/{Repo}";

    /// <summary>Short display name such as <c>owner/repo#12</c>.</summary>
    public string DisplayName => $"{Owner}/{Repo}#{Number}";

    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out PullRequestUrl? pullRequest,
        [NotNullWhen(false)] out string? error)
    {
        pullRequest = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = $"expected a pull request URL like {ExpectedShape}";
            return false;
        }

        var trimmed = text.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = $"'{trimmed}' is not a URL; expected {ExpectedShape}";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host != "github.com" && host != "www.github.com")
        {
            error = $"'{trimmed}' is not a github.com URL; only GitHub pull requests are supported";
            return false;
        }

        if (!uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
        {
            error = $"'{trimmed}' is not a pull request URL; expected {ExpectedShape}";
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 || !segments[2].Equals("pull", StringComparison.OrdinalIgnoreCase))
        {
            error = $"'{trimmed}' is not a pull request URL; expected {ExpectedShape}";
            return false;
        }

        var owner = segments[0];
        var repo = segments[1];
        if (!IsValidOwner(owner) || !IsValidRepo(repo))
        {
            error = $"'{trimmed}' does not name a valid GitHub owner and repository";
            return false;
        }

        if (!NumberPattern().IsMatch(segments[3])
            || !int.TryParse(segments[3], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number <= 0)
        {
            error = $"'{trimmed}' does not end in a valid pull request number";
            return false;
        }

        pullRequest = new PullRequestUrl(owner, repo, number);
        return true;
    }

    /// <summary>GitHub user and organisation names: letters, digits and single inner hyphens.</summary>
    public static bool IsValidOwner(string owner) => OwnerPattern().IsMatch(owner);

    /// <summary>GitHub repository names: letters, digits, '.', '-' and '_', never '.' or '..'.</summary>
    public static bool IsValidRepo(string repo) =>
        repo is not "." and not ".." && RepoPattern().IsMatch(repo);

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}$")]
    private static partial Regex OwnerPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepoPattern();

    [GeneratedRegex("^[0-9]{1,9}$")]
    private static partial Regex NumberPattern();
}
