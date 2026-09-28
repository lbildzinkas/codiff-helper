using System.Text;

namespace CodiffHelper;

/// <summary><c>codiff-helper [&lt;url&gt;] --done [--force]</c>: delete a review copy codiff-helper created.</summary>
internal static class FinishReview
{
    private const int ListedItems = 5;

    public static async Task<int> RunAsync(DoneCommand command, AppServices services, CancellationToken cancellationToken)
    {
        var root = services.ResolveRoot();
        var store = ReviewStore.OpenExisting(root)
                    ?? throw new UserFacingException($"there are no review copies: {services.Display(root)} does not exist");

        var review = command.PullRequest is { } pullRequest
            ? LoadByUrl(store, pullRequest, services)
            : LoadFromCurrentDirectory(store, services);

        if (!command.Force)
        {
            var state = await ReadLocalStateAsync(review, services, cancellationToken).ConfigureAwait(false);
            if (state.HasLocalWork)
            {
                await services.Error.WriteAsync(DescribeBlockers(review, state, services)).ConfigureAwait(false);
                return ExitCodes.Failure;
            }
        }

        var currentDirectory = ReviewStore.RealPath(services.CurrentDirectory);
        var wasInside = currentDirectory is not null && ReviewStore.IsSameOrInside(currentDirectory, review.Path);

        store.Delete(review);
        await services.Out.WriteLineAsync($"Deleted {review.PullRequest.DisplayName} ({services.Display(review.Path)})")
            .ConfigureAwait(false);

        if (wasInside)
        {
            var landing = NearestExisting(review.Path, store.Root);
            await services.Out.WriteLineAsync($"Your shell is still in the deleted folder; run: cd {services.Display(landing)}")
                .ConfigureAwait(false);
        }
        return ExitCodes.Success;
    }

    private static Review LoadByUrl(ReviewStore store, PullRequestUrl pullRequest, AppServices services)
    {
        var folder = store.FolderFor(pullRequest);
        if (!ReviewStore.Occupied(folder))
            throw new UserFacingException($"there is no review copy of {pullRequest.DisplayName} at {services.Display(folder)}");

        var review = store.TryLoad(folder, out var reason)
                     ?? throw new UserFacingException($"refusing to delete {services.Display(folder)}: {reason}");
        if (review.Path != folder)
            throw new UserFacingException($"refusing to delete {services.Display(folder)}: it resolves to {review.Path} through a symlink");
        return review;
    }

    private static Review LoadFromCurrentDirectory(ReviewStore store, AppServices services)
    {
        var folder = ReviewStore.FindEnclosingRecordFolder(services.CurrentDirectory)
                     ?? throw new UserFacingException(
                         "not inside a review folder; run it from inside one, or pass the URL: codiff-helper <pull-request-url> --done");

        return store.TryLoad(folder, out var reason)
               ?? throw new UserFacingException($"refusing to delete {services.Display(folder)}: {reason}");
    }

    private static async Task<LocalState> ReadLocalStateAsync(Review review, AppServices services, CancellationToken cancellationToken)
    {
        var known = review.Record.LastSyncedHead is { } synced ? new[] { synced } : [];
        try
        {
            return await services.Git.GetLocalStateAsync(review.Path, known, cancellationToken).ConfigureAwait(false);
        }
        catch (UserFacingException ex)
        {
            throw new UserFacingException($"could not check {services.Display(review.Path)} for local work ({ex.Message}); use --force to delete it anyway");
        }
    }

    internal static string DescribeBlockers(Review review, LocalState state, AppServices services)
    {
        var text = new StringBuilder();
        text.AppendLine($"codiff-helper: not deleting {services.Display(review.Path)}; it has work that is not on GitHub:");
        AppendItems(text, state.ChangedFiles, "changed file", "changed files");
        AppendItems(text, state.UntrackedFiles, "untracked file", "untracked files");
        AppendItems(text, state.UnpushedCommits, "local commit not on GitHub", "local commits not on GitHub");
        if (state.StashCount > 0) text.AppendLine($"  {Count(state.StashCount, "stash entry", "stash entries")}");

        var target = review.PullRequest.Url;
        text.AppendLine($"Push or discard that work, or delete it anyway with: codiff-helper {target} --done --force");
        return text.ToString();
    }

    private static void AppendItems(StringBuilder text, IReadOnlyList<string> items, string singular, string plural)
    {
        if (items.Count == 0) return;
        var shown = string.Join(", ", items.Take(ListedItems));
        var more = items.Count > ListedItems ? $", and {items.Count - ListedItems} more" : "";
        text.AppendLine($"  {Count(items.Count, singular, plural)}: {shown}{more}");
    }

    private static string Count(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";

    private static string NearestExisting(string deleted, string root)
    {
        for (var directory = Path.GetDirectoryName(deleted); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (Directory.Exists(directory) && ReviewStore.IsSameOrInside(directory, root)) return directory;
        }
        return root;
    }
}
