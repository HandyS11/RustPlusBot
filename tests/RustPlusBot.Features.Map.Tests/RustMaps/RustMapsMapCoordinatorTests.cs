using RustPlusBot.Abstractions.Map;
using RustPlusBot.Features.Map.RustMaps;

namespace RustPlusBot.Features.Map.Tests.RustMaps;

public sealed class RustMapsMapCoordinatorTests
{
    private static readonly RustMapsMapKey Key = new(4000, 12345);
    private static readonly Guid Server = Guid.NewGuid();

    private static RustMapsReadyMap Ready(string imageUrl = "https://img/x.png",
        string? pageUrl = "https://rustmaps/x") =>
        new(imageUrl, pageUrl, []);

    [Fact]
    public void Unseen_key_snapshots_as_idle()
    {
        var c = new RustMapsMapCoordinator();
        Assert.Equal(RustMapsGenerationState.Idle, c.Snapshot(Key).State);
    }

    [Fact]
    public void Register_is_idempotent_and_accumulates_requesters()
    {
        var c = new RustMapsMapCoordinator();
        var s2 = Guid.NewGuid();
        c.Register(Key, 1UL, Server);
        c.Register(Key, 1UL, Server); // same requester again
        c.Register(Key, 1UL, s2); // second server, same (size,seed)

        Assert.Equal(2, c.Requesters(Key).Count);
        Assert.Contains(Key, c.PendingKeys());
    }

    [Fact]
    public void Lifecycle_transitions_and_ready_caches_the_image()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        Assert.True(c.TrySetGenerating(Key, "map-1"));
        Assert.Equal(RustMapsGenerationState.Generating, c.Snapshot(Key).State);
        Assert.Equal("map-1", c.Snapshot(Key).MapId);

        c.SetReady(Key, Ready());
        var snap = c.Snapshot(Key);
        Assert.Equal(RustMapsGenerationState.Ready, snap.State);
        Assert.Equal("https://img/x.png", snap.Ready!.ImageUrl);
        Assert.Equal("https://rustmaps/x", snap.Ready.RustMapsUrl);
    }

    [Fact]
    public void Terminal_states_are_not_pending_and_are_not_reset_by_register()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        c.SetFailed(Key);
        c.Register(Key, 1UL, Server); // must not revive

        Assert.Equal(RustMapsGenerationState.Failed, c.Snapshot(Key).State);
        Assert.DoesNotContain(Key, c.PendingKeys());
    }

    [Fact]
    public void Resolve_returns_the_view_once_the_render_is_verified_for_that_server()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        c.SetReady(Key, Ready());
        c.SetMatch(Key, 1UL, Server, RustMapsMapMatch.Match);

        var resolution = c.Resolve(1UL, Server, Key.Size, Key.Seed);

        Assert.Equal(InfoMapStatus.Verified, resolution.Status);
        Assert.Equal("https://img/x.png", resolution.View!.ImageUrl);
        Assert.Equal("https://rustmaps/x", resolution.View.RustMapsPageUrl);
    }

    [Fact]
    public void Resolve_withholds_a_ready_render_until_it_is_verified()
    {
        // The whole point of the check: a render must never reach a server before it has been matched
        // against that server's own world.
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        c.SetReady(Key, Ready());

        var resolution = c.Resolve(1UL, Server, Key.Size, Key.Seed);

        Assert.Equal(InfoMapStatus.Pending, resolution.Status);
        Assert.Null(resolution.View);
    }

    [Fact]
    public void Resolve_reports_a_mismatch_without_a_view()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        c.SetReady(Key, Ready());
        c.SetMatch(Key, 1UL, Server, RustMapsMapMatch.Mismatch);

        var resolution = c.Resolve(1UL, Server, Key.Size, Key.Seed);

        Assert.Equal(InfoMapStatus.Mismatched, resolution.Status);
        Assert.Null(resolution.View);
    }

    [Fact]
    public void A_verdict_is_per_server_not_per_key()
    {
        // Two servers can share a (size, seed) while only one of them actually runs that world.
        var other = Guid.NewGuid();
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        c.Register(Key, 1UL, other);
        c.SetReady(Key, Ready());
        c.SetMatch(Key, 1UL, Server, RustMapsMapMatch.Match);
        c.SetMatch(Key, 1UL, other, RustMapsMapMatch.Mismatch);

        Assert.Equal(InfoMapStatus.Verified, c.Resolve(1UL, Server, Key.Size, Key.Seed).Status);
        Assert.Equal(InfoMapStatus.Mismatched, c.Resolve(1UL, other, Key.Size, Key.Seed).Status);
    }

    [Fact]
    public void Unknown_verdicts_are_not_recorded()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        c.SetMatch(Key, 1UL, Server, RustMapsMapMatch.Unknown);

        Assert.Equal(RustMapsMapMatch.Unknown, c.MatchFor(Key, 1UL, Server));
    }

    [Fact]
    public void Resolve_is_pending_when_not_ready()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        c.TrySetGenerating(Key, "map-1");

        Assert.Equal(InfoMapStatus.Pending, c.Resolve(1UL, Server, Key.Size, Key.Seed).Status);
    }

    [Fact]
    public void Resolve_is_pending_for_an_unseen_key()
    {
        var c = new RustMapsMapCoordinator();

        Assert.Equal(InfoMapStatus.Pending, c.Resolve(1UL, Server, Key.Size, Key.Seed).Status);
    }

    [Fact]
    public void ReadyKeys_lists_only_ready_keys_with_requesters()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        Assert.DoesNotContain(Key, c.ReadyKeys());

        c.SetReady(Key, Ready());

        Assert.Contains(Key, c.ReadyKeys());
        Assert.DoesNotContain(Key, c.PendingKeys());
    }
}
