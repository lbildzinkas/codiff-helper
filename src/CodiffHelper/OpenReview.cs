namespace CodiffHelper;

/// <summary><c>codiff-helper &lt;url&gt;</c>: clone or update the review copy, then open it in Codiff.</summary>
internal static class OpenReview
{
    public static async Task<int> RunAsync(OpenCommand command, AppServices services, CancellationToken cancellationToken)
    {
        var pullRequest = command.PullRequest;
        var root = services.ResolveRoot();

        services.Codiff.EnsureInstalled();
        await services.GitHub.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var info = await services.GitHub.GetPullRequestAsync(pullRequest, cancellationToken).ConfigureAwait(false);

        var store = ReviewStore.OpenOrCreate(root);
        var folder = store.FolderFor(pullRequest);
        if (ReviewStore.Occupied(folder))
            await RefreshAsync(store, folder, pullRequest, info, services, cancellationToken).ConfigureAwait(false);
        else
            await CreateAsync(store, folder, pullRequest, info, services, cancellationToken).ConfigureAwait(false);

        if (!info.IsOpen)
            await services.Out.WriteLineAsync($"note: {pullRequest.DisplayName} is {info.State.ToLowerInvariant()}").ConfigureAwait(false);

        await services.Codiff.LaunchAsync(
            new CodiffLaunch(folder, pullRequest, command.Walkthrough, command.Agent), cancellationToken).ConfigureAwait(false);
        await services.Out.WriteLineAsync(
            $"Opened {pullRequest.DisplayName} in Codiff from {services.Display(folder)}").ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task CreateAsync(
        ReviewStore store, string folder, PullRequestUrl pullRequest, PullRequestInfo info, AppServices services,
        CancellationToken cancellationToken)
    {
        var repoDirectory = Path.GetDirectoryName(folder)!;
        Directory.CreateDirectory(repoDirectory);
        if (ReviewStore.RealPath(repoDirectory) != repoDirectory)
            throw new UserFacingException($"refusing to use {repoDirectory}: it resolves outside {store.Root} through a symlink");

        await services.Out.WriteLineAsync($"Cloning {pullRequest.RepoSlug} into {services.Display(folder)}").ConfigureAwait(false);

        // Build the copy under a hidden name and move it into place only once it is complete, so an interrupted
        // clone never leaves a half-made review folder behind.
        var staging = Path.Combine(repoDirectory, $".{Path.GetFileName(folder)}.partial-{Guid.NewGuid():N}");
        try
        {
            await services.GitHub.CloneAsync(pullRequest, staging, cancellationToken).ConfigureAwait(false);
            await services.GitHub.CheckoutAsync(staging, pullRequest, cancellationToken).ConfigureAwait(false);
            var head = await services.Git.GetHeadCommitAsync(staging, cancellationToken).ConfigureAwait(false);
            (OwnershipRecord.Create(pullRequest, services.Time.GetUtcNow()) with
            {
                Title = info.Title,
                State = info.State,
                LastSyncedHead = head,
            }).WriteTo(staging);

            if (ReviewStore.Occupied(folder))
                throw new UserFacingException($"{services.Display(folder)} appeared while cloning; run the command again");
            Directory.Move(staging, folder);
        }
        finally
        {
            DeleteStaging(staging);
        }
    }

    private static async Task RefreshAsync(
        ReviewStore store, string folder, PullRequestUrl pullRequest, PullRequestInfo info, AppServices services,
        CancellationToken cancellationToken)
    {
        var review = store.TryLoad(folder, out var reason);
        if (review is null)
            throw new UserFacingException($"cannot use {services.Display(folder)}: {reason}; move it away and try again");
        if (review.Path != folder)
            throw new UserFacingException($"refusing to use {services.Display(folder)}: it resolves to {review.Path} through a symlink");

        var known = review.Record.LastSyncedHead is { } synced ? new[] { synced } : [];
        var state = await services.Git.GetLocalStateAsync(folder, known, cancellationToken).ConfigureAwait(false);
        if (state.HasWorkingTreeChanges || state.UnpushedCommits.Count > 0)
        {
            await services.Error.WriteLineAsync(
                $"warning: {services.Display(folder)} has local edits or commits, so it was left as it is and not updated to the latest commits")
                .ConfigureAwait(false);
            return;
        }

        var before = await services.Git.GetHeadCommitAsync(folder, cancellationToken).ConfigureAwait(false);
        try
        {
            await services.GitHub.CheckoutAsync(folder, pullRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (UserFacingException ex)
        {
            await services.Error.WriteLineAsync($"warning: could not update the copy ({ex.Message}); opening it as it is")
                .ConfigureAwait(false);
            return;
        }

        var after = await services.Git.GetHeadCommitAsync(folder, cancellationToken).ConfigureAwait(false);
        (review.Record with
        {
            Title = info.Title,
            State = info.State,
            LastSyncedHead = after,
            UpdatedAt = services.Time.GetUtcNow(),
        }).WriteTo(folder);

        var message = before == after
            ? $"{pullRequest.DisplayName} is already up to date"
            : $"Updated {pullRequest.DisplayName} to the latest commits ({Short(after)})";
        await services.Out.WriteLineAsync(message).ConfigureAwait(false);
    }

    private static void DeleteStaging(string staging)
    {
        var info = new DirectoryInfo(staging);
        if (!info.Exists || info.LinkTarget is not null) return;
        try
        {
            info.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover hidden staging folder is never listed or opened as a review.
        }
    }

    private static string Short(string? commit) => commit is { Length: > 7 } ? commit[..7] : commit ?? "unknown";
}
