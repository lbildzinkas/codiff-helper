namespace CodiffHelper;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Usage = 2;
    public const int Cancelled = 130;
}

/// <summary>An expected failure whose message is shown to the user as a single line.</summary>
internal sealed class UserFacingException(string message, int exitCode = ExitCodes.Failure) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
