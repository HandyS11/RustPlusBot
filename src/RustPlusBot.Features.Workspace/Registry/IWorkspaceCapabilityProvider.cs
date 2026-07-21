namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>
/// Answers whether an optional workspace capability currently applies to a scope. A
/// <see cref="ChannelSpec"/> naming a capability is provisioned only while its provider reports
/// available, and its channel is deleted when it does not.
/// </summary>
internal interface IWorkspaceCapabilityProvider
{
    /// <summary>The capability name this provider answers for (matches <see cref="ChannelSpec.Capability"/>).</summary>
    string Capability { get; }

    /// <summary>Reports whether the capability currently applies to the given scope.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id, or null for the global scope.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when the capability's channels should exist.</returns>
    ValueTask<bool> IsAvailableAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken);
}
