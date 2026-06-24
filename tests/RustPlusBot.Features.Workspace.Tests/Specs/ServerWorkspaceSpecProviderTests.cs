using RustPlusBot.Features.Workspace;
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
}
