namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Aggregates contributed spec providers into ordered, scope-filtered views.</summary>
/// <param name="channelProviders">All channel spec providers.</param>
/// <param name="messageProviders">All message spec providers.</param>
internal sealed class WorkspaceRegistry(
    IEnumerable<IChannelSpecProvider> channelProviders,
    IEnumerable<IMessageSpecProvider> messageProviders) : IWorkspaceRegistry
{
    private readonly List<ChannelSpec> _channels = [.. channelProviders.SelectMany(p => p.GetChannelSpecs())];
    private readonly List<MessageSpec> _messages = [.. messageProviders.SelectMany(p => p.GetMessageSpecs())];

    /// <inheritdoc />
    public IReadOnlyList<ChannelSpec> GetChannelSpecs(WorkspaceScope scope) =>
        [.. _channels.Where(s => s.Scope == scope).OrderBy(s => s.Order).ThenBy(s => s.Key, StringComparer.Ordinal)];

    /// <inheritdoc />
    public IReadOnlyList<MessageSpec> GetMessageSpecs(WorkspaceScope scope) =>
        [.. _messages.Where(s => s.Scope == scope)];
}
