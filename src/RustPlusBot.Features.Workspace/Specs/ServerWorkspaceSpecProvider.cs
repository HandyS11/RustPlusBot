using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Specs;

/// <summary>Contributes the per-server category's channels and messages (just #info in 1a).</summary>
internal sealed class ServerWorkspaceSpecProvider : IChannelSpecProvider, IMessageSpecProvider
{
    /// <inheritdoc />
    public IEnumerable<ChannelSpec> GetChannelSpecs() =>
    [
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerInfo, "channel.info.name",
            ChannelPermissionProfile.ReadOnly, 0),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerTeamChat, "channel.teamchat.name",
            ChannelPermissionProfile.Interactive, 1),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerEvents, "channel.events.name",
            ChannelPermissionProfile.ReadOnly, 2),
    ];

    /// <inheritdoc />
    public IEnumerable<MessageSpec> GetMessageSpecs() =>
    [
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerInfo, WorkspaceChannelKeys.ServerInfo),
    ];
}
