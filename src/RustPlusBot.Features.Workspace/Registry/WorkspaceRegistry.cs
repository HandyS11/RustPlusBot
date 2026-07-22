namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Aggregates contributed spec providers into ordered, scope-filtered views.</summary>
/// <param name="channelProviders">All channel spec providers.</param>
/// <param name="messageProviders">All message spec providers.</param>
/// <param name="capabilityProviders">All capability providers, indexed by capability name.</param>
internal sealed class WorkspaceRegistry(
    IEnumerable<IChannelSpecProvider> channelProviders,
    IEnumerable<IMessageSpecProvider> messageProviders,
    IEnumerable<IWorkspaceCapabilityProvider> capabilityProviders) : IWorkspaceRegistry
{
    private readonly Dictionary<string, IWorkspaceCapabilityProvider> _capabilities =
        capabilityProviders.ToDictionary(p => p.Capability, StringComparer.Ordinal);

    private readonly List<ChannelSpec> _channels = [.. channelProviders.SelectMany(p => p.GetChannelSpecs())];
    private readonly List<MessageSpec> _messages = [.. messageProviders.SelectMany(p => p.GetMessageSpecs())];

    /// <inheritdoc />
    public ValueTask<bool> IsCapabilityAvailableAsync(string capability,
        ulong guildId,
        Guid? serverId,
        CancellationToken cancellationToken) =>
        _capabilities.TryGetValue(capability, out var provider)
            ? provider.IsAvailableAsync(guildId, serverId, cancellationToken)
            : ValueTask.FromResult(false);

    /// <inheritdoc />
    public IReadOnlyList<ChannelSpec> GetChannelSpecs(WorkspaceScope scope) =>
        [.. _channels.Where(s => s.Scope == scope).OrderBy(s => s.Order).ThenBy(s => s.Key, StringComparer.Ordinal)];

    /// <inheritdoc />
    public IReadOnlyList<MessageSpec> GetMessageSpecs(WorkspaceScope scope) =>
        [.. _messages.Where(s => s.Scope == scope)];
}
