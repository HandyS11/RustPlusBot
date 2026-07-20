using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Specs;

namespace RustPlusBot.Features.Workspace.Tests.Specs;

public sealed class ServerWorkspaceSpecProviderTests
{
    [Fact]
    public void Contributes_teamchat_as_interactive_per_server_channel()
    {
        var provider = new ServerWorkspaceSpecProvider();

        var teamchat = provider.GetChannelSpecs()
            .Single(c => c.Key == WorkspaceChannelKeys.ServerTeamChat);

        Assert.Equal(WorkspaceScope.PerServer, teamchat.Scope);
        Assert.Equal(ChannelPermissionProfile.Interactive, teamchat.Permissions);
        Assert.Equal("channel.teamchat.name", teamchat.NameKey);
    }

    [Fact]
    public void Contributes_storagemonitors_as_interactive_per_server_channel()
    {
        var provider = new ServerWorkspaceSpecProvider();

        var storagemonitors = provider.GetChannelSpecs()
            .Single(c => c.Key == WorkspaceChannelKeys.ServerStorageMonitors);

        Assert.Equal(WorkspaceScope.PerServer, storagemonitors.Scope);
        Assert.Equal(ChannelPermissionProfile.Interactive, storagemonitors.Permissions);
        Assert.Equal("channel.storagemonitors.name", storagemonitors.NameKey);
    }

    [Fact]
    public void Info_channel_messages_are_declared_in_render_order()
    {
        var specs = new ServerWorkspaceSpecProvider().GetMessageSpecs()
            .Where(s => s.ChannelKey == "info")
            .Select(s => s.Key)
            .ToArray();

        // Discord orders by creation time, so declaration order is the on-screen order:
        // map image, then status, then events, then team.
        Assert.Equal(["server.info.map", "server.info", "server.events", "server.team"], specs);
    }

    [Fact]
    public void Every_per_server_message_targets_a_declared_channel()
    {
        var provider = new ServerWorkspaceSpecProvider();
        var channels = provider.GetChannelSpecs().Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        Assert.All(provider.GetMessageSpecs(), s => Assert.Contains(s.ChannelKey, channels));
    }
}
