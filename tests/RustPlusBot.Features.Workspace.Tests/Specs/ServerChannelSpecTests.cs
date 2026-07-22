using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Specs;

namespace RustPlusBot.Features.Workspace.Tests.Specs;

/// <summary>Locks the per-server channel set and its on-screen ordering.</summary>
public sealed class ServerChannelSpecTests
{
    private static readonly ChannelSpec[] Specs = [.. new ServerWorkspaceSpecProvider().GetChannelSpecs()];

    [Fact]
    public void Declares_player_events_read_only_right_after_events()
    {
        var playerEvents = Assert.Single(Specs, s => s.Key == WorkspaceChannelKeys.ServerPlayerEvents);

        Assert.Equal("playerevents", WorkspaceChannelKeys.ServerPlayerEvents);
        Assert.Equal("channel.playerevents.name", playerEvents.NameKey);
        Assert.Equal(ChannelPermissionProfile.ReadOnly, playerEvents.Permissions);
        Assert.Equal(WorkspaceScope.PerServer, playerEvents.Scope);
        Assert.Null(playerEvents.Capability);
    }

    [Fact]
    public void Orders_player_events_between_events_and_map()
    {
        var keysInOrder = Specs.OrderBy(s => s.Order).Select(s => s.Key).ToArray();

        Assert.Equal(
        [
            WorkspaceChannelKeys.ServerInfo,
            WorkspaceChannelKeys.ServerTeamChat,
            WorkspaceChannelKeys.ServerClanChat,
            WorkspaceChannelKeys.ServerClanInfo,
            WorkspaceChannelKeys.ServerEvents,
            WorkspaceChannelKeys.ServerPlayerEvents,
            WorkspaceChannelKeys.ServerMap,
            WorkspaceChannelKeys.ServerSwitches,
            WorkspaceChannelKeys.ServerAlarms,
            WorkspaceChannelKeys.ServerStorageMonitors,
        ], keysInOrder);
    }

    [Fact]
    public void Assigns_a_unique_order_to_every_channel()
    {
        Assert.Equal(Specs.Length, Specs.Select(s => s.Order).Distinct().Count());
    }
}
