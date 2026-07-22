using RustPlusBot.Features.Clans.Messages;

namespace RustPlusBot.Features.Workspace.Tests.Messages;

/// <summary>
///     Pins that the clan renderers' key literals still match the keys the reconciler looks renderers
///     up by. Features.Clans can see Features.Workspace's internal <see cref="WorkspaceMessageKeys" />
///     (it is granted <c>InternalsVisibleTo</c>) but deliberately declares its own public constants
///     instead, so the two sides are independent literals. The reconciler compares them with
///     <c>StringComparer.Ordinal</c>, so a silent rename on either side would unhook a renderer
///     without any other test catching it.
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
