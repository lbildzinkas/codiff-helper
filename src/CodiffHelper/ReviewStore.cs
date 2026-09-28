using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CodiffHelper;

/// <summary>A review folder under the reviews root that carries a valid ownership record.</summary>
internal sealed record Review(string Path, PullRequestUrl PullRequest, OwnershipRecord Record);

/// <summary>
/// The reviews root and the folders in it, laid out as <c>&lt;root&gt;/&lt;owner&gt;/&lt;repo&gt;/pr-&lt;number&gt;</c>.
/// Every path is resolved to its real location first, and only folders exactly three levels under the real root
/// that carry a matching ownership record are ever treated as reviews.
/// </summary>
internal sealed partial class ReviewStore
{
    private ReviewStore(string root) => Root = root;

    /// <summary>The real (symlink-free) path of the reviews root.</summary>
    public string Root { get; }

    /// <summary>Opens an existing root, or returns null when it does not exist.</summary>
    public static ReviewStore? OpenExisting(string root) =>
        RealPath(root) is { } real && Directory.Exists(real) ? new ReviewStore(real) : null;

    /// <summary>Opens the root, creating it when needed.</summary>
    public static ReviewStore OpenOrCreate(string root)
    {
        Directory.CreateDirectory(root);
        return OpenExisting(root) ?? throw new UserFacingException($"could not open the reviews folder {root}");
    }

    /// <summary>Where the review folder for <paramref name="pullRequest"/> lives (owner and repo in lower case).</summary>
    public string FolderFor(PullRequestUrl pullRequest) =>
        Path.Combine(Root, pullRequest.Owner.ToLowerInvariant(), pullRequest.Repo.ToLowerInvariant(),
            $"pr-{pullRequest.Number.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>True when something (a folder, file or link) already sits at <paramref name="path"/>.</summary>
    public static bool Occupied(string path)
    {
        var info = new FileInfo(path);
        return info.Exists || info.LinkTarget is not null || Directory.Exists(path);
    }

    /// <summary>
    /// Loads the review at <paramref name="path"/>, or explains why it is not one codiff-helper may act on.
    /// </summary>
    public Review? TryLoad(string path, out string reason)
    {
        var real = RealPath(path);
        if (real is null || !Directory.Exists(real))
        {
            reason = $"{path} does not exist";
            return null;
        }

        if (!TryParseLocation(real, out var owner, out var repo, out var number))
        {
            reason = $"{real} is not a review folder inside {Root}";
            return null;
        }

        var record = OwnershipRecord.TryRead(real, out var pullRequest);
        if (record is null || pullRequest is null)
        {
            reason = $"{real} was not created by codiff-helper";
            return null;
        }

        if (!pullRequest.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase)
            || !pullRequest.Repo.Equals(repo, StringComparison.OrdinalIgnoreCase)
            || pullRequest.Number != number)
        {
            reason = $"{real} holds a codiff-helper record for {pullRequest.DisplayName}, which does not match its location";
            return null;
        }

        reason = "";
        return new Review(real, pullRequest, record);
    }

    /// <summary>Every valid review under the root; symlinked folders are skipped, never followed.</summary>
    public IReadOnlyList<Review> List()
    {
        var reviews = new List<Review>();
        foreach (var ownerDirectory in PlainSubdirectories(Root))
            foreach (var repoDirectory in PlainSubdirectories(ownerDirectory))
                foreach (var reviewDirectory in PlainSubdirectories(repoDirectory))
                {
                    if (TryLoad(reviewDirectory, out _) is { } review) reviews.Add(review);
                }

        return reviews
            .OrderBy(r => r.PullRequest.Owner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PullRequest.Repo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PullRequest.Number)
            .ToList();
    }

    /// <summary>
    /// Walks up from <paramref name="directory"/> to the nearest folder carrying a codiff-helper record.
    /// Returns its real path, or null when there is none.
    /// </summary>
    public static string? FindEnclosingRecordFolder(string directory)
    {
        for (var current = RealPath(directory); current is not null; current = Path.GetDirectoryName(current))
        {
            if (File.Exists(OwnershipRecord.PathIn(current))) return current;
        }
        return null;
    }

    /// <summary>
    /// Deletes a review folder after re-checking, immediately before deleting, that it is still a valid review
    /// strictly inside the root. Empty owner and repo folders left behind are removed; the root never is.
    /// </summary>
    public void Delete(Review review)
    {
        var current = TryLoad(review.Path, out var reason);
        if (current is null || current.Path != review.Path)
            throw new UserFacingException($"refusing to delete {review.Path}: {reason}");

        if (!IsStrictlyInside(current.Path, Root))
            throw new UserFacingException($"refusing to delete {current.Path}: it is not inside {Root}");

        Directory.Delete(current.Path, recursive: true);

        var repoDirectory = Path.GetDirectoryName(current.Path);
        var ownerDirectory = repoDirectory is null ? null : Path.GetDirectoryName(repoDirectory);
        DeleteIfEmpty(repoDirectory);
        DeleteIfEmpty(ownerDirectory);
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or inside it (real paths expected).</summary>
    public static bool IsSameOrInside(string path, string folder) =>
        path == folder || IsStrictlyInside(path, folder);

    /// <summary>Resolves symlinks, <c>.</c> and <c>..</c>; returns null when the path does not exist.</summary>
    public static string? RealPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var resolved = NativeRealPath(path, IntPtr.Zero);
        if (resolved == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUTF8(resolved);
        }
        finally
        {
            NativeFree(resolved);
        }
    }

    private bool TryParseLocation(string realPath, out string owner, out string repo, out int number)
    {
        owner = repo = "";
        number = 0;
        if (!IsStrictlyInside(realPath, Root)) return false;

        var segments = Path.GetRelativePath(Root, realPath).Split(Path.DirectorySeparatorChar);
        if (segments.Length != 3) return false;

        var match = ReviewFolderName().Match(segments[2]);
        if (!PullRequestUrl.IsValidOwner(segments[0]) || !PullRequestUrl.IsValidRepo(segments[1]) || !match.Success)
            return false;

        owner = segments[0];
        repo = segments[1];
        return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    }

    private void DeleteIfEmpty(string? directory)
    {
        if (directory is null || !IsStrictlyInside(directory, Root)) return;
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch (IOException)
        {
            // Something appeared in the meantime; leaving the folder is harmless.
        }
    }

    private static bool IsStrictlyInside(string path, string folder)
    {
        var prefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static IEnumerable<string> PlainSubdirectories(string directory)
    {
        IEnumerable<DirectoryInfo> children;
        try
        {
            children = new DirectoryInfo(directory).EnumerateDirectories().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (child.LinkTarget is null) yield return child.FullName;
        }
    }

    [GeneratedRegex("^pr-([1-9][0-9]{0,8})$")]
    private static partial Regex ReviewFolderName();

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr NativeRealPath(string path, IntPtr resolvedPath);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void NativeFree(IntPtr pointer);
}
