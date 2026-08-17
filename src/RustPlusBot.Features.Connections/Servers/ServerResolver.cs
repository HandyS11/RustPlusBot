using RustPlusBot.Localization;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Connections.Servers;

/// <summary>Picks the target server for a slash command from an optional (autocompleted) server argument.</summary>
/// <param name="servers">The server service used to list the guild's servers.</param>
/// <param name="localizer">Resolves the error messages.</param>
public sealed class ServerResolver(IServerService servers, ILocalizer localizer)
{
    /// <summary>Resolves the target server, or returns a localized error.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverArg">The raw server argument (a server id string) or null.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved server, or an error message.</returns>
    public async Task<ServerResolution> ResolveAsync(
        ulong guildId,
        string? serverArg,
        string culture,
        CancellationToken cancellationToken)
    {
        var known = await servers.ListAsync(guildId, cancellationToken).ConfigureAwait(false);
        if (known.Count == 0)
        {
            return new ServerResolution(null, null, localizer.Get("command.server.none", culture));
        }

        if (string.IsNullOrWhiteSpace(serverArg))
        {
            if (known.Count == 1)
            {
                return new ServerResolution(known[0].Id, known[0].Name, null);
            }

            return new ServerResolution(null, null, localizer.Get("command.server.specify", culture));
        }

        if (Guid.TryParse(serverArg, out var id))
        {
            var match = known.FirstOrDefault(s => s.Id == id);
            if (match is not null)
            {
                return new ServerResolution(match.Id, match.Name, null);
            }
        }

        return new ServerResolution(null, null, localizer.Get("command.server.unknown", culture));
    }
}
