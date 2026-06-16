namespace RustPlusBot.Features.Commands.Dispatching;

/// <summary>A parsed command line: a lowercase name plus zero or more whitespace-split args.</summary>
/// <param name="Name">The lowercase command name (without the prefix).</param>
/// <param name="Args">The whitespace-split arguments.</param>
public readonly record struct CommandLine(string Name, IReadOnlyList<string> Args)
{
    /// <summary>Parses <paramref name="rawLine"/> against <paramref name="prefix"/>.</summary>
    /// <param name="prefix">The command prefix (e.g. "!").</param>
    /// <param name="rawLine">The raw in-game line.</param>
    /// <param name="command">The parsed command on success.</param>
    /// <returns>True when the line is a command for this prefix.</returns>
    public static bool TryParse(string prefix, string rawLine, out CommandLine command)
    {
        command = default;
        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(rawLine))
        {
            return false;
        }

        var trimmed = rawLine.Trim();
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = trimmed[prefix.Length..];
        var tokens = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var name = tokens[0].ToLowerInvariant();
        var args = tokens.Length > 1 ? tokens[1..] : [];
        command = new CommandLine(name, args);
        return true;
    }
}
