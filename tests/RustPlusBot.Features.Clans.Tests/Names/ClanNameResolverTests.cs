using NSubstitute;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Tests.Names;

public sealed class ClanNameResolverTests
{
    private const ulong Guild = 42UL;

    private static readonly Guid Server = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Returns_cached_names_for_known_ids()
    {
        var store = Substitute.For<IClanStore>();
        store.GetNamesAsync(Guild, Server, Arg.Any<IReadOnlyCollection<ulong>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<ulong, string>>(
                new Dictionary<ulong, string>
                {
                    [1UL] = "Ada", [2UL] = "Grace"
                }));

        var resolved = await new ClanNameResolver(store)
            .ResolveAsync(Guild, Server, [1UL, 2UL], CancellationToken.None);

        Assert.Equal("Ada", resolved[1UL]);
        Assert.Equal("Grace", resolved[2UL]);
    }

    [Fact]
    public async Task Falls_back_to_a_steam_profile_link_for_unknown_ids()
    {
        var store = Substitute.For<IClanStore>();
        store.GetNamesAsync(Guild, Server, Arg.Any<IReadOnlyCollection<ulong>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<ulong, string>>(new Dictionary<ulong, string>()));

        var resolved = await new ClanNameResolver(store)
            .ResolveAsync(Guild, Server, [76561198000000000UL], CancellationToken.None);

        Assert.Equal(
            "[76561198000000000](https://steamcommunity.com/profiles/76561198000000000)",
            resolved[76561198000000000UL]);
    }

    [Fact]
    public async Task Covers_every_requested_id()
    {
        var store = Substitute.For<IClanStore>();
        store.GetNamesAsync(Guild, Server, Arg.Any<IReadOnlyCollection<ulong>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<ulong, string>>(
                new Dictionary<ulong, string>
                {
                    [1UL] = "Ada", [3UL] = "  "
                }));

        var resolved = await new ClanNameResolver(store)
            .ResolveAsync(Guild, Server, [1UL, 2UL, 3UL], CancellationToken.None);

        Assert.Equal(3, resolved.Count);
        Assert.True(resolved.ContainsKey(1UL));
        Assert.True(resolved.ContainsKey(2UL));

        // A blank cached name is no better than no name at all.
        Assert.Equal("[3](https://steamcommunity.com/profiles/3)", resolved[3UL]);
    }

    [Fact]
    public async Task Returns_an_empty_map_for_an_empty_request_without_querying_the_store()
    {
        var store = Substitute.For<IClanStore>();

        var resolved = await new ClanNameResolver(store).ResolveAsync(Guild, Server, [], CancellationToken.None);

        Assert.Empty(resolved);
        await store.DidNotReceiveWithAnyArgs()
            .GetNamesAsync(default, Guid.Empty, default!, default);
    }
}
