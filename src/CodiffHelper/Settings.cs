namespace CodiffHelper;

internal static class Settings
{
    public const string RootVariable = "CODIFF_HELPER_ROOT";
    public const string DefaultRootName = "Reviews";

    /// <summary>
    /// The reviews root: <c>CODIFF_HELPER_ROOT</c> when set (a leading <c>~</c> means the home folder),
    /// otherwise <c>~/Reviews</c>.
    /// </summary>
    public static string ResolveRoot(Func<string, string?> getEnvironmentVariable, string homeDirectory)
    {
        var configured = getEnvironmentVariable(RootVariable)?.Trim();
        if (string.IsNullOrEmpty(configured)) return Path.Combine(homeDirectory, DefaultRootName);

        var expanded = configured == "~" ? homeDirectory
            : configured.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(homeDirectory, configured[2..])
            : configured;
        if (!Path.IsPathRooted(expanded))
            throw new UserFacingException($"{RootVariable} must be an absolute path (it is '{configured}')");

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        if (full == Path.GetPathRoot(full))
            throw new UserFacingException($"{RootVariable} must not be the file system root");
        return full;
    }

    /// <summary>Shows paths under the home folder as <c>~/...</c>.</summary>
    public static string Display(string path, string homeDirectory)
    {
        var home = Path.TrimEndingDirectorySeparator(homeDirectory);
        if (home.Length == 0 || home == "/") return path;
        if (path == home) return "~";
        return path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }
}
