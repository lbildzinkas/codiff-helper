namespace CodiffHelper;

/// <summary><c>codiff-helper --list</c>: show the review copies on disk.</summary>
internal static class ListReviews
{
    private const int MaxConcurrentLookups = 6;

    public static async Task<int> RunAsync(AppServices services, CancellationToken cancellationToken)
    {
        var root = services.ResolveRoot();
        var store = ReviewStore.OpenExisting(root);
        var reviews = store?.List() ?? [];
        if (reviews.Count == 0)
        {
            await services.Out.WriteLineAsync($"No review copies in {services.Display(root)}").ConfigureAwait(false);
            return ExitCodes.Success;
        }

        var gitHubReady = await IsGitHubReadyAsync(services, cancellationToken).ConfigureAwait(false);
        using var throttle = new SemaphoreSlim(MaxConcurrentLookups);
        var rows = await Task.WhenAll(reviews.Select(async review =>
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await DescribeAsync(review, gitHubReady, services, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                throttle.Release();
            }
        })).ConfigureAwait(false);

        await services.Out.WriteAsync(FormatTable(rows)).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    internal sealed record Row(string Review, string State, string Local, string Title, string Path);

    internal static string FormatTable(IReadOnlyList<Row> rows)
    {
        var header = new Row("REVIEW", "STATE", "LOCAL", "TITLE", "PATH");
        IReadOnlyList<Row> all = [header, .. rows];
        var reviewWidth = all.Max(r => r.Review.Length);
        var stateWidth = all.Max(r => r.State.Length);
        var localWidth = all.Max(r => r.Local.Length);
        var titleWidth = all.Max(r => r.Title.Length);

        var lines = all.Select(r =>
            $"{r.Review.PadRight(reviewWidth)}  {r.State.PadRight(stateWidth)}  {r.Local.PadRight(localWidth)}  {r.Title.PadRight(titleWidth)}  {r.Path}");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static async Task<Row> DescribeAsync(Review review, bool gitHubReady, AppServices services, CancellationToken cancellationToken)
    {
        var title = review.Record.Title;
        var state = review.Record.State;
        if (gitHubReady)
        {
            try
            {
                var info = await services.GitHub.GetPullRequestAsync(review.PullRequest, cancellationToken).ConfigureAwait(false);
                title = info.Title;
                state = info.State;
            }
            catch (UserFacingException)
            {
                // Fall back to what was recorded when the copy was last opened.
            }
        }

        string local;
        try
        {
            var known = review.Record.LastSyncedHead is { } synced ? new[] { synced } : [];
            var localState = await services.Git.GetLocalStateAsync(review.Path, known, cancellationToken).ConfigureAwait(false);
            local = localState.HasLocalWork ? "local changes" : "clean";
        }
        catch (UserFacingException)
        {
            local = "unknown";
        }

        return new Row(
            review.PullRequest.DisplayName,
            state?.ToLowerInvariant() ?? "unknown",
            local,
            Truncate(title ?? "(title unavailable)", 60),
            services.Display(review.Path));
    }

    private static async Task<bool> IsGitHubReadyAsync(AppServices services, CancellationToken cancellationToken)
    {
        try
        {
            await services.GitHub.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (UserFacingException ex)
        {
            await services.Error.WriteLineAsync($"note: showing saved titles and states ({ex.Message})").ConfigureAwait(false);
            return false;
        }
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
}
