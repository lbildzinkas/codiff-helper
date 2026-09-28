namespace CodiffHelper;

internal abstract record CliCommand;

internal sealed record OpenCommand(PullRequestUrl PullRequest, bool Walkthrough, string? Agent) : CliCommand;

/// <summary><c>--done</c>; with no pull request it works on the review folder around the current directory.</summary>
internal sealed record DoneCommand(PullRequestUrl? PullRequest, bool Force) : CliCommand;

internal sealed record ListCommand : CliCommand;

internal sealed record HelpCommand : CliCommand;

internal sealed record VersionCommand : CliCommand;

internal static class CommandLine
{
    public const string Usage =
        """
        codiff-helper - open a GitHub pull request in Codiff from a fresh local copy, and delete the copy when done.

        Usage:
          codiff-helper <pull-request-url> [--no-walkthrough] [--agent <name>]
          codiff-helper <pull-request-url> --done [--force]
          codiff-helper --done [--force]
          codiff-helper --list
          codiff-helper --help | --version

        Options:
          --no-walkthrough   Open Codiff without starting the AI walkthrough.
          --agent <name>     Agent backend passed through to Codiff (for example: pi, claude, codex).
          --done             Delete the review copy. Without a URL, use the review folder you are in.
          --force            With --done, delete even when the copy has work that is not on GitHub.
          --list             Show the review copies on disk.
          -h, --help         Show this help.
          -v, --version      Show the version.

        Review copies live in ~/Reviews/<owner>/<repo>/pr-<number>; set CODIFF_HELPER_ROOT to use another folder.
        """;

    /// <summary>Parses arguments, throwing a usage error (exit code 2) for anything it does not understand.</summary>
    public static CliCommand Parse(IReadOnlyList<string> args)
    {
        if (args.Any(a => a is "-h" or "--help")) return new HelpCommand();
        if (args.Any(a => a is "-v" or "--version")) return new VersionCommand();

        string? url = null;
        string? agent = null;
        bool done = false, force = false, list = false, noWalkthrough = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--done": done = true; break;
                case "--force": force = true; break;
                case "--list": list = true; break;
                case "--no-walkthrough": noWalkthrough = true; break;
                case "--agent":
                    if (i + 1 >= args.Count) throw Error("--agent needs a name, for example: --agent pi");
                    agent = ValidateAgent(args[++i]);
                    break;
                case not null when arg.StartsWith('-'):
                    throw Error($"unknown option '{arg}'");
                default:
                    if (url is not null) throw Error($"expected one pull request URL, got '{url}' and '{arg}'");
                    url = arg;
                    break;
            }
        }

        if (list)
        {
            if (url is not null || done || force || noWalkthrough || agent is not null)
                throw Error("--list takes no URL or other options");
            return new ListCommand();
        }

        if (force && !done) throw Error("--force only works together with --done");

        PullRequestUrl? pullRequest = null;
        if (url is not null && !PullRequestUrl.TryParse(url, out pullRequest, out var error)) throw Error(error);

        if (done)
        {
            if (noWalkthrough || agent is not null) throw Error("--done cannot be combined with --no-walkthrough or --agent");
            return new DoneCommand(pullRequest, force);
        }

        if (pullRequest is null) throw Error("missing pull request URL");
        return new OpenCommand(pullRequest, Walkthrough: !noWalkthrough, agent);
    }

    private static string ValidateAgent(string agent)
    {
        if (agent.Length == 0 || agent.StartsWith('-') || agent.Any(char.IsWhiteSpace))
            throw Error($"'{agent}' is not an agent name; for example: --agent pi");
        return agent;
    }

    private static UserFacingException Error(string message) =>
        new($"{message} (see codiff-helper --help)", ExitCodes.Usage);
}
