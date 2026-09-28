using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodiffHelper;

/// <summary>
/// Marks a folder as created by codiff-helper. It lives in the folder's <c>.git</c> directory so it never shows
/// up as a working-tree change, and <c>--done</c> and <c>--list</c> only act on folders that carry a valid one.
/// </summary>
internal sealed record OwnershipRecord
{
    public const string FileName = "codiff-helper.json";
    public const string ToolName = "codiff-helper";
    public const int CurrentFormatVersion = 1;

    [JsonPropertyName("tool")] public string? Tool { get; init; }
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>The pull request head last checked out from GitHub, so it counts as already on GitHub.</summary>
    [JsonPropertyName("lastSyncedHead")] public string? LastSyncedHead { get; init; }

    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }

    public static OwnershipRecord Create(PullRequestUrl pullRequest, DateTimeOffset now) =>
        new() { Tool = ToolName, FormatVersion = CurrentFormatVersion, Url = pullRequest.Url, CreatedAt = now };

    public static string PathIn(string reviewFolder) => Path.Combine(reviewFolder, ".git", FileName);

    /// <summary>Reads the record in <paramref name="reviewFolder"/>, returning null when it is absent or not valid.</summary>
    public static OwnershipRecord? TryRead(string reviewFolder, out PullRequestUrl? pullRequest)
    {
        pullRequest = null;
        var gitDirectory = Path.Combine(reviewFolder, ".git");
        var recordPath = PathIn(reviewFolder);
        if (!IsPlainDirectory(gitDirectory) || !IsPlainFile(recordPath)) return null;

        OwnershipRecord? record;
        try
        {
            record = JsonSerializer.Deserialize(File.ReadAllText(recordPath), OwnershipJsonContext.Default.OwnershipRecord);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (record is null
            || record.Tool != ToolName
            || record.FormatVersion != CurrentFormatVersion
            || record.CreatedAt == default
            || !PullRequestUrl.TryParse(record.Url, out pullRequest, out _))
            return null;

        return record;
    }

    /// <summary>Writes the record atomically into <paramref name="reviewFolder"/>'s <c>.git</c> directory.</summary>
    public void WriteTo(string reviewFolder)
    {
        var gitDirectory = Path.Combine(reviewFolder, ".git");
        if (!IsPlainDirectory(gitDirectory))
            throw new UserFacingException($"{reviewFolder} has no .git directory to hold the codiff-helper record");

        var recordPath = PathIn(reviewFolder);
        var temporaryPath = recordPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, OwnershipJsonContext.Default.OwnershipRecord));
        File.Move(temporaryPath, recordPath, overwrite: true);
    }

    private static bool IsPlainDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        return info.Exists && info.LinkTarget is null;
    }

    private static bool IsPlainFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.LinkTarget is null;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OwnershipRecord))]
internal sealed partial class OwnershipJsonContext : JsonSerializerContext;
