using RustPlusBot.Features.Map.RustMaps;

namespace RustPlusBot.Features.Map.Tests.RustMaps;

public sealed class RustMapsMapCoordinatorTests
{
    private static readonly RustMapsMapKey Key = new(4000, 12345);
    private static readonly Guid Server = Guid.NewGuid();

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

        c.SetReady(Key, new RustMapsReadyMap("https://img/x.png", "https://rustmaps/x"));
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
    public void GetReady_returns_the_view_when_ready()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        c.SetReady(Key, new RustMapsReadyMap("https://img/x.png", "https://rustmaps/x"));

        var view = c.GetReady(Key.Size, Key.Seed);

        Assert.NotNull(view);
        Assert.Equal("https://img/x.png", view!.ImageUrl);
        Assert.Equal("https://rustmaps/x", view.RustMapsPageUrl);
    }

    [Fact]
    public void GetReady_returns_null_when_not_ready()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        c.TrySetGenerating(Key, "map-1");

        Assert.Null(c.GetReady(Key.Size, Key.Seed));
    }

    [Fact]
    public void GetReady_returns_null_for_an_unseen_key()
    {
        var c = new RustMapsMapCoordinator();

        Assert.Null(c.GetReady(Key.Size, Key.Seed));
    }
}
