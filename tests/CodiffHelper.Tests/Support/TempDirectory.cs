namespace CodiffHelper.Tests.Support;

/// <summary>A fresh folder under the system temp folder; disposing deletes only this folder.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        var created = Directory.CreateTempSubdirectory("codiff-helper-tests-").FullName;
        Path = ReviewStore.RealPath(created) ?? created;
    }

    /// <summary>The real (symlink-free) path of the folder.</summary>
    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        if (!Path.Contains("codiff-helper-tests-", StringComparison.Ordinal)) return;
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
