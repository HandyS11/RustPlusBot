namespace RustPlusBot.Features.Wipes.Detection;

/// <summary>Checks a freshly connected server against its persisted wipe baseline.</summary>
internal interface IWipeDetector
{
    /// <summary>Reads live info/world, diffs against the baseline, and publishes <see cref="RustPlusBot.Abstractions.Events.ServerWipedEvent"/> on a wipe.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server that just connected.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the check (and any event publication) has finished.</returns>
    Task CheckAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
