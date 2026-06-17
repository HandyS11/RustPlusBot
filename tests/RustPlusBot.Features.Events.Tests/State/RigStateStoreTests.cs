using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Events.Tests.State;

public sealed class RigStateStoreTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    private static (RigStateStore Store, TestClock Clock) Create()
    {
        var clock = new TestClock();
        var options = Options.Create(new ConnectionOptions
        {
            RigActiveWindow = TimeSpan.FromMinutes(15), RigOfflineWindow = TimeSpan.FromMinutes(15),
        });
        return (new RigStateStore(clock, options), clock);
    }

    private static RigStateChangedEvent Activated(RigKind rig) =>
        new(Guild, Server, rig, RigEventKind.Activated, 100f, 200f, null);

    [Fact]
    public void Untracked_rig_reads_online_with_no_timer()
    {
        var (store, _) = Create();
        var state = store.Get(Guild, Server, RigKind.Small);
        Assert.Equal(RigStatus.Online, state.Status);
        Assert.Null(state.Remaining);
    }

    [Fact]
    public void Apply_activated_sets_active_with_remaining()
    {
        var (store, _) = Create();
        store.Apply(Activated(RigKind.Small));
        var state = store.Get(Guild, Server, RigKind.Small);
        Assert.Equal(RigStatus.Active, state.Status);
        Assert.Equal(TimeSpan.FromMinutes(15), state.Remaining);
    }

    [Fact]
    public void Advance_active_past_window_yields_crate_lootable_and_goes_offline()
    {
        var (store, clock) = Create();
        store.Apply(Activated(RigKind.Small));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);

        var crossings = store.Advance(clock.UtcNow);

        Assert.Single(crossings);
        Assert.Equal(RigEventKind.CrateLootable, crossings[0].Kind);
        Assert.Equal(RigKind.Small, crossings[0].Rig);
        Assert.Equal(100f, crossings[0].X);
        Assert.Equal(RigStatus.Offline, store.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public void Advance_offline_past_window_yields_respawned_and_returns_to_online_untracked()
    {
        var (store, clock) = Create();
        store.Apply(Activated(RigKind.Small));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        store.Advance(clock.UtcNow); // -> Offline
        clock.UtcNow = clock.UtcNow.AddMinutes(15);

        var crossings = store.Advance(clock.UtcNow); // -> Online (Respawned), then dropped

        Assert.Single(crossings);
        Assert.Equal(RigEventKind.Respawned, crossings[0].Kind);
        Assert.Equal(RigStatus.Online, store.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public void Get_settles_overdue_transition_on_read()
    {
        var (store, clock) = Create();
        store.Apply(Activated(RigKind.Small));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);

        // No Advance call; reading must settle Active -> Offline.
        Assert.Equal(RigStatus.Offline, store.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public void Activated_in_any_state_resets_to_active()
    {
        var (store, clock) = Create();
        store.Apply(Activated(RigKind.Small));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        store.Advance(clock.UtcNow); // Offline
        store.Apply(Activated(RigKind.Small)); // ground-truth override
        Assert.Equal(RigStatus.Active, store.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public void Clear_drops_tracked_rigs()
    {
        var (store, _) = Create();
        store.Apply(Activated(RigKind.Small));
        store.Clear(Guild, Server);
        Assert.Equal(RigStatus.Online, store.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public void Advance_returns_nothing_for_untracked_or_within_window()
    {
        var (store, clock) = Create();
        Assert.Empty(store.Advance(clock.UtcNow));
        store.Apply(Activated(RigKind.Small));
        Assert.Empty(store.Advance(clock.UtcNow)); // still within active window
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    }
}
