using RustPlusBot.Features.Clans.Messages;

namespace RustPlusBot.Features.Workspace.Tests.Messages;

/// <summary>
///     Pins that the clan renderers' key literals (declared in Features.Clans, which cannot see
///     Features.Workspace's internal <see cref="WorkspaceMessageKeys" />) still match the keys the
///     reconciler looks renderers up by. The reconciler compares with <c>StringComparer.Ordinal</c>,
///     so a silent rename on either side would unhook a renderer without any other test catching it.
/// </summary>
public sealed class ClanMessageKeyParityTests
{
    [Fact]
    public void Clan_renderer_keys_match_the_workspace_message_key_constants()
    {
        Assert.Equal(WorkspaceMessageKeys.ClanOverview, ClanOverviewMessageRenderer.Key);
        Assert.Equal(WorkspaceMessageKeys.ClanRoster, ClanRosterMessageRenderer.Key);
        Assert.Equal(WorkspaceMessageKeys.ClanInvites, ClanInvitesMessageRenderer.Key);
    }
}
