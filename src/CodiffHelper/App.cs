using System.Reflection;

namespace CodiffHelper;

/// <summary>Everything the commands touch outside their own logic, so tests can substitute each part.</summary>
internal sealed record AppServices(
    TextWriter Out,
    TextWriter Error,
    Func<string, string?> GetEnvironmentVariable,
    string HomeDirectory,
    string CurrentDirectory,
    IGit Git,
    IGitHub GitHub,
    ICodiff Codiff,
    TimeProvider Time)
{
    public static AppServices ForThisProcess()
    {
        var runner = new ProcessRunner();
        return new AppServices(
            Console.Out,
            Console.Error,
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Directory.GetCurrentDirectory(),
            new GitCli(runner),
            new GhCli(runner),
            new CodiffCli(runner, Environment.GetEnvironmentVariable),
            TimeProvider.System);
    }

    public string Display(string path) => Settings.Display(path, HomeDirectory);

    public string ResolveRoot() => Settings.ResolveRoot(GetEnvironmentVariable, HomeDirectory);
}

internal static class App
{
    public static string Version =>
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, AppServices services, CancellationToken cancellationToken = default)
    {
        try
        {
            return CommandLine.Parse(args) switch
            {
                HelpCommand => Print(services.Out, CommandLine.Usage),
                VersionCommand => Print(services.Out, $"codiff-helper {Version}"),
                OpenCommand open => await OpenReview.RunAsync(open, services, cancellationToken).ConfigureAwait(false),
                DoneCommand done => await FinishReview.RunAsync(done, services, cancellationToken).ConfigureAwait(false),
                ListCommand => await ListReviews.RunAsync(services, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("unhandled command"),
            };
        }
        catch (UserFacingException ex)
        {
            await services.Error.WriteLineAsync($"codiff-helper: {ex.Message}").ConfigureAwait(false);
            return ex.ExitCode;
        }
        catch (CommandNotFoundException ex)
        {
            var hint = ex.Command == "git" ? "; install it with: xcode-select --install" : "";
            await services.Error.WriteLineAsync($"codiff-helper: {ex.Command} is not installed or not on PATH{hint}").ConfigureAwait(false);
            return ExitCodes.Failure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await services.Error.WriteLineAsync("codiff-helper: cancelled").ConfigureAwait(false);
            return ExitCodes.Cancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await services.Error.WriteLineAsync($"codiff-helper: {ex.Message}").ConfigureAwait(false);
            return ExitCodes.Failure;
        }
    }

    private static int Print(TextWriter writer, string text)
    {
        writer.WriteLine(text);
        return ExitCodes.Success;
    }
}
