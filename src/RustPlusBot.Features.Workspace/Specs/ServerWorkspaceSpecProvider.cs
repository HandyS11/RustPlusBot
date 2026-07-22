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
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerClanChat, "channel.clanchat.name",
            ChannelPermissionProfile.Interactive, 2, WorkspaceCapabilities.Clan),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerClanInfo, "channel.claninfo.name",
            ChannelPermissionProfile.ReadOnly, 3, WorkspaceCapabilities.Clan),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerEvents, "channel.events.name",
            ChannelPermissionProfile.ReadOnly, 4),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerMap, "channel.map.name",
            ChannelPermissionProfile.ReadOnly, 5),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerSwitches, "channel.switches.name",
            ChannelPermissionProfile.Interactive, 6),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerAlarms, "channel.alarms.name",
            ChannelPermissionProfile.Interactive, 7),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerStorageMonitors, "channel.storagemonitors.name",
            ChannelPermissionProfile.Interactive, 8),
    ];

    /// <inheritdoc />
    public IEnumerable<MessageSpec> GetMessageSpecs() =>
    [
        // Declaration order IS the on-screen order (Discord orders messages by creation time), and
        // the reconciler re-posts later messages to repair drift. Keep: map image, status, events, team.
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerInfoMap, WorkspaceChannelKeys.ServerInfo),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerInfo, WorkspaceChannelKeys.ServerInfo),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerEvents, WorkspaceChannelKeys.ServerInfo),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerTeam, WorkspaceChannelKeys.ServerInfo),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerMap, WorkspaceChannelKeys.ServerMap),

        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ClanOverview, WorkspaceChannelKeys.ServerClanInfo),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ClanRoster, WorkspaceChannelKeys.ServerClanInfo),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ClanInvites, WorkspaceChannelKeys.ServerClanInfo),
    ];
}
