using System.Globalization;
using Discord;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Clans.Messages;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Clans;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Clans.Tests.Messages;

public sealed class ClanRendererTests
{
    private const ulong Guild = 42UL;

    private static readonly Guid Server = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly MessageRenderContext Context = new(Guild, Server, "en");

    // ----- Overview -------------------------------------------------------------------------

    [Fact]
    public async Task Overview_returns_an_empty_payload_without_a_server_id()
    {
        var store = Substitute.For<IClanStore>();
        var renderer = new ClanOverviewMessageRenderer(store, Resolver(), Substitute.For<IConnectionStore>(),
            Localizer());

        var payload = await renderer.RenderAsync(new MessageRenderContext(Guild, null, "en"), CancellationToken.None);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Overview_returns_an_empty_payload_when_no_clan_is_stored()
    {
        var store = Substitute.For<IClanStore>();
        store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ClanSnapshot?>(null));
        var renderer = new ClanOverviewMessageRenderer(store, Resolver(), Substitute.For<IConnectionStore>(),
            Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Overview_shows_the_clan_name_score_and_member_count()
    {
        var clan = Clan(
            name: "Wolves",
            maxMemberCount: 8,
            score: 1234,
            members: [Member(1UL, 1), Member(2UL, 2)]);
        var renderer = new ClanOverviewMessageRenderer(Store(clan), Resolver(), Substitute.For<IConnectionStore>(),
            Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        Assert.Contains("clan.overview.title", payload.Embed.Title, StringComparison.Ordinal);
        Assert.Contains("Wolves", payload.Embed.Title, StringComparison.Ordinal);
        Assert.Equal("1234", FieldValue(payload.Embed, "clan.overview.score"));
        Assert.Equal("2/8", FieldValue(payload.Embed, "clan.overview.members"));
    }

    [Fact]
    public async Task Overview_shows_the_motd_and_its_author()
    {
        var clan = Clan(
            motd: "Hold the fort",
            motdAuthor: 2UL,
            members: [Member(1UL, 1), Member(2UL, 2)]);
        var resolver = Resolver(new Dictionary<ulong, string>
        {
            [2UL] = "Grace"
        });
        var renderer = new ClanOverviewMessageRenderer(Store(clan), resolver, Substitute.For<IConnectionStore>(),
            Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        var motd = FieldValue(payload.Embed, "clan.overview.motd");
        Assert.Contains("Hold the fort", motd, StringComparison.Ordinal);
        Assert.Contains("clan.overview.motdby", motd, StringComparison.Ordinal);
        Assert.Contains("Grace", motd, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overview_uses_the_clan_colour_when_set()
    {
        // Packed ARGB: the alpha byte must be masked off before it reaches Discord.
        var clan = Clan(color: unchecked((int)0xFF336699));
        var renderer = new ClanOverviewMessageRenderer(Store(clan), Resolver(), Substitute.For<IConnectionStore>(),
            Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        Assert.Equal(new Color(0x336699u), payload.Embed.Color);
    }

    [Fact]
    public async Task Overview_shows_the_set_motd_button_when_the_active_player_may_set_it()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Leader", canSetMotd: true)],
            members: [Member(7UL, 1)]);
        var renderer = new ClanOverviewMessageRenderer(Store(clan), Resolver(), Connections(7UL), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Components);
        var button = payload.Components.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components)
            .OfType<ButtonComponent>()
            .Single();
        Assert.Equal(
            "clan:motd:" + Server.ToString("D", CultureInfo.InvariantCulture),
            button.CustomId);
    }

    [Fact]
    public async Task Overview_hides_the_set_motd_button_when_the_active_player_may_not()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Member")],
            members: [Member(7UL, 1)]);
        var renderer = new ClanOverviewMessageRenderer(Store(clan), Resolver(), Connections(7UL), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Overview_hides_the_set_motd_button_when_the_active_player_is_not_a_member()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Leader", canSetMotd: true)],
            members: [Member(7UL, 1)]);
        var renderer = new ClanOverviewMessageRenderer(Store(clan), Resolver(), Connections(999UL), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Overview_hides_the_set_motd_button_when_there_is_no_active_credential()
    {
        // The granted-role member's SteamId (1UL) matches the clan's default Creator (see Clan()),
        // so a mutation that falls back to the creator when there is no credential would
        // incorrectly resolve a member and show the button instead of hiding it.
        var clan = Clan(
            roles: [Role(1, 0, "Leader", canSetMotd: true)],
            members: [Member(1UL, 1)]);
        var connections = Substitute.For<IConnectionStore>();
        connections.GetActiveCredentialAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PlayerCredential?>(null));
        var renderer = new ClanOverviewMessageRenderer(Store(clan), Resolver(), connections, Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Overview_hides_the_set_motd_button_when_the_role_is_unknown()
    {
        // The active player is a clan member, but their RoleId (99) matches none of the clan's roles.
        var clan = Clan(
            roles: [Role(1, 0, "Leader", canSetMotd: true)],
            members: [Member(7UL, 99)]);
        var renderer = new ClanOverviewMessageRenderer(Store(clan), Resolver(), Connections(7UL), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.Null(payload.Components);
    }

    // ----- Roster ---------------------------------------------------------------------------

    [Fact]
    public async Task Roster_returns_an_empty_payload_without_a_server_id()
    {
        var store = Substitute.For<IClanStore>();
        var renderer = new ClanRosterMessageRenderer(store, Resolver(), Localizer());

        var payload = await renderer.RenderAsync(new MessageRenderContext(Guild, null, "en"), CancellationToken.None);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Roster_returns_an_empty_payload_when_no_clan_is_stored()
    {
        var store = Substitute.For<IClanStore>();
        store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ClanSnapshot?>(null));
        var renderer = new ClanRosterMessageRenderer(store, Resolver(), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Roster_groups_members_by_role_in_rank_order()
    {
        // RoleId intentionally does not correlate with Rank (Leader=rank0/id9, Officer=rank1/id2,
        // Member=rank2/id5), so a mutation that orders by RoleId alone (dropping the Rank key)
        // would visibly reorder the fields below.
        var clan = Clan(
            roles: [Role(9, 0, "Leader"), Role(2, 1, "Officer"), Role(5, 2, "Member")],
            members: [Member(1UL, 9), Member(2UL, 2), Member(3UL, 5)]);
        var resolver = Resolver();
        var renderer = new ClanRosterMessageRenderer(Store(clan), resolver, Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        Assert.Equal(3, payload.Embed.Fields.Length);
        Assert.Contains("Leader", payload.Embed.Fields[0].Name, StringComparison.Ordinal);
        Assert.Contains("Officer", payload.Embed.Fields[1].Name, StringComparison.Ordinal);
        Assert.Contains("Member", payload.Embed.Fields[2].Name, StringComparison.Ordinal);

        // One batched name-resolution call for the whole roster, not one per member.
        await resolver.Received(1).ResolveAsync(
            Guild, Server, Arg.Any<IReadOnlyCollection<ulong>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Roster_truncates_a_role_body_that_would_exceed_the_field_value_limit()
    {
        var members = Enumerable.Range(1, 60)
            .Select(i => Member((ulong)i, 1, joined: DateTimeOffset.UnixEpoch))
            .ToList();
        var clan = Clan(roles: [Role(1, 0, "Leader")], members: members);
        var renderer = new ClanRosterMessageRenderer(Store(clan), Resolver(), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        var value = payload.Embed.Fields[0].Value;
        Assert.True(value.Length <= 1024, $"Field value length {value.Length} exceeds Discord's 1024 cap.");

        var lines = value.Split('\n');
        var noticeLine = lines[^1];
        Assert.Contains("clan.roster.truncated", noticeLine, StringComparison.Ordinal);

        // Compute the expected omitted count from what actually rendered rather than hard-coding
        // it, so this stays valid if line formatting changes.
        var renderedMemberLines = lines.Length - 1;
        var expectedOmitted = members.Count - renderedMemberLines;
        var omittedToken = noticeLine.Split(' ')[^1];
        Assert.Equal(expectedOmitted.ToString(CultureInfo.InvariantCulture), omittedToken);
    }

    [Fact]
    public async Task Roster_puts_online_members_first_within_a_role()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Leader")],
            members:
            [
                Member(1UL, 1, online: false, joined: DateTimeOffset.UnixEpoch),
                Member(2UL, 1, online: true, joined: DateTimeOffset.UnixEpoch.AddDays(5)),
            ]);
        var renderer = new ClanRosterMessageRenderer(Store(clan), Resolver(), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        var body = payload.Embed.Fields[0].Value;
        Assert.True(
            body.IndexOf("P2", StringComparison.Ordinal) < body.IndexOf("P1", StringComparison.Ordinal),
            body);
    }

    [Fact]
    public async Task Roster_marks_online_and_offline_members_distinctly()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Leader")],
            members: [Member(1UL, 1, online: true), Member(2UL, 1, online: false)]);
        var renderer = new ClanRosterMessageRenderer(Store(clan), Resolver(), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        var body = payload.Embed.Fields[0].Value;
        Assert.Contains("🟢 P1", body, StringComparison.Ordinal);
        Assert.Contains("clan.roster.joined", body, StringComparison.Ordinal);
        Assert.Contains("⚫ P2", body, StringComparison.Ordinal);
        Assert.Contains("clan.roster.lastseen", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Roster_shows_notes_when_present()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Leader")],
            members: [Member(1UL, 1, notes: "Trial member")]);
        var renderer = new ClanRosterMessageRenderer(Store(clan), Resolver(), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        var body = payload.Embed.Fields[0].Value;
        Assert.Contains("clan.roster.notes", body, StringComparison.Ordinal);
        Assert.Contains("Trial member", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Roster_falls_back_to_a_profile_link_for_an_unknown_name()
    {
        const string link = "[1](https://steamcommunity.com/profiles/1)";
        var clan = Clan(roles: [Role(1, 0, "Leader")], members: [Member(1UL, 1)]);
        var resolver = Resolver(new Dictionary<ulong, string>
        {
            [1UL] = link
        });
        var renderer = new ClanRosterMessageRenderer(Store(clan), resolver, Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        Assert.Contains(link, payload.Embed.Fields[0].Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Roster_lists_members_whose_role_is_unknown_under_a_fallback_group()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Leader")],
            members: [Member(1UL, 1), Member(2UL, 99)]);
        var renderer = new ClanRosterMessageRenderer(Store(clan), Resolver(), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        Assert.Equal(2, payload.Embed.Fields.Length);
        Assert.Equal("clan.roster.unknownrole", payload.Embed.Fields[1].Name);
        Assert.Contains("P2", payload.Embed.Fields[1].Value, StringComparison.Ordinal);
    }

    // ----- Invites --------------------------------------------------------------------------

    [Fact]
    public async Task Invites_returns_an_empty_payload_without_a_server_id()
    {
        var store = Substitute.For<IClanStore>();
        var renderer = new ClanInvitesMessageRenderer(store, Resolver(), Localizer());

        var payload = await renderer.RenderAsync(new MessageRenderContext(Guild, null, "en"), CancellationToken.None);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Invites_returns_an_empty_payload_when_no_clan_is_stored()
    {
        var store = Substitute.For<IClanStore>();
        store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ClanSnapshot?>(null));
        var renderer = new ClanInvitesMessageRenderer(store, Resolver(), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Invites_returns_an_empty_payload_when_there_are_none()
    {
        var renderer = new ClanInvitesMessageRenderer(Store(Clan()), Resolver(), Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Invites_lists_the_invitee_and_the_recruiter()
    {
        var clan = Clan(invites: [new ClanInviteSnapshot(5UL, 9UL, DateTimeOffset.UnixEpoch)]);
        var resolver = Resolver(new Dictionary<ulong, string>
        {
            [5UL] = "Newbie", [9UL] = "Grace"
        });
        var renderer = new ClanInvitesMessageRenderer(Store(clan), resolver, Localizer());

        var payload = await renderer.RenderAsync(Context, CancellationToken.None);

        Assert.NotNull(payload.Embed);
        Assert.Contains("clan.invites.line", payload.Embed.Description, StringComparison.Ordinal);
        Assert.Contains("Newbie", payload.Embed.Description, StringComparison.Ordinal);
        Assert.Contains("Grace", payload.Embed.Description, StringComparison.Ordinal);
    }

    // ----- Helpers --------------------------------------------------------------------------

    private static string FieldValue(IEmbed embed, string name) =>
        embed.Fields.Single(f => string.Equals(f.Name, name, StringComparison.Ordinal)).Value;

    /// <summary>
    ///     A localizer that echoes the key it was asked for, plus any format arguments, so assertions
    ///     match on stable keys and on the data actually passed rather than on English copy.
    /// </summary>
    private static ILocalizer Localizer()
    {
        var localizer = Substitute.For<ILocalizer>();
        localizer.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(0));
        localizer.Get(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(ci => $"{ci.ArgAt<string>(0)} {string.Join(' ', ci.ArgAt<object[]>(2))}");
        return localizer;
    }

    private static IClanStore Store(ClanSnapshot clan)
    {
        var store = Substitute.For<IClanStore>();
        store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ClanSnapshot?>(clan));
        return store;
    }

    private static IConnectionStore Connections(ulong steamId)
    {
        var connections = Substitute.For<IConnectionStore>();
        connections.GetActiveCredentialAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PlayerCredential?>(new PlayerCredential
            {
                SteamId = steamId
            }));
        return connections;
    }

    /// <summary>Resolves every requested id, using the supplied names where given and "P{id}" otherwise.</summary>
    /// <param name="known">Names to use for specific ids.</param>
    /// <returns>The substituted resolver.</returns>
    private static IClanNameResolver Resolver(Dictionary<ulong, string>? known = null)
    {
        var resolver = Substitute.For<IClanNameResolver>();
        resolver.ResolveAsync(
                Arg.Any<ulong>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<ulong>>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyDictionary<ulong, string>>(
                ci.ArgAt<IReadOnlyCollection<ulong>>(2).ToDictionary(
                    id => id,
                    id => known is not null && known.TryGetValue(id, out var name)
                        ? name
                        : $"P{id.ToString(CultureInfo.InvariantCulture)}")));
        return resolver;
    }

    private static ClanRoleSnapshot Role(int roleId, int rank, string name, bool canSetMotd = false) =>
        new(roleId, rank, name, canSetMotd, false, false, false, false, false, false, false, false);

    private static ClanMemberSnapshot Member(
        ulong steamId,
        int roleId,
        bool online = true,
        DateTimeOffset? joined = null,
        string? notes = null) =>
        new(steamId, roleId, joined ?? DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, notes, online);

    private static ClanSnapshot Clan(
        string name = "Wolves",
        string? motd = null,
        ulong? motdAuthor = null,
        int? color = null,
        int? maxMemberCount = null,
        long? score = null,
        IReadOnlyList<ClanRoleSnapshot>? roles = null,
        IReadOnlyList<ClanMemberSnapshot>? members = null,
        IReadOnlyList<ClanInviteSnapshot>? invites = null) => new(
        1,
        name,
        DateTimeOffset.UnixEpoch,
        1UL,
        motd,
        motd is null ? null : DateTimeOffset.UnixEpoch,
        motdAuthor,
        null,
        color,
        maxMemberCount,
        score,
        roles ?? [Role(1, 0, "Leader"), Role(2, 1, "Member")],
        members ?? [Member(1UL, 1)],
        invites ?? []);
}
