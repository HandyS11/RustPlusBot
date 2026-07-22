namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Aggregates contributed spec providers into ordered, scope-filtered views.</summary>
internal sealed class WorkspaceRegistry : IWorkspaceRegistry
{
    private readonly Dictionary<string, IWorkspaceCapabilityProvider> _capabilities;
    private readonly List<ChannelSpec> _channels;
    private readonly List<MessageSpec> _messages;

    /// <summary>Initializes the registry and validates that every gated channel has a provider.</summary>
    /// <param name="channelProviders">All channel spec providers.</param>
    /// <param name="messageProviders">All message spec providers.</param>
    /// <param name="capabilityProviders">All capability providers, indexed by capability name.</param>
    /// <exception cref="InvalidOperationException">
    ///     A channel spec names a capability that no registered provider answers for.
    /// </exception>
    public WorkspaceRegistry(
        IEnumerable<IChannelSpecProvider> channelProviders,
        IEnumerable<IMessageSpecProvider> messageProviders,
        IEnumerable<IWorkspaceCapabilityProvider> capabilityProviders)
    {
        _capabilities = capabilityProviders.ToDictionary(p => p.Capability, StringComparer.Ordinal);
        _channels = [.. channelProviders.SelectMany(p => p.GetChannelSpecs())];
        _messages = [.. messageProviders.SelectMany(p => p.GetMessageSpecs())];

        // Fail the host rather than the guild's data: the reconciler reads "no provider" as "capability
        // unavailable", and unavailable means it DELETES the gated channel and everything in it. A host
        // that composes AddWorkspace() without the module owning a capability would silently destroy
        // those channels on its first heal, so refuse to start instead.
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var spec in _channels)
        {
            if (spec.Capability is { } capability && !_capabilities.ContainsKey(capability))
            {
                missing.Add(capability);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "No IWorkspaceCapabilityProvider is registered for workspace channel capability(ies): " +
                string.Join(", ", missing) +
                ". The host must compose the feature module that provides them; without it the reconciler " +
                "would treat the gated channels as unavailable and delete them along with their history.");
        }
    }

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
