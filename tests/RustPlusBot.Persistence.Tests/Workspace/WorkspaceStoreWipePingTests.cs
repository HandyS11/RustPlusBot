using RustPlusBot.Abstractions.Time;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Persistence.Tests.Workspace;

/// <summary>Wipe-ping accessor tests for <see cref="WorkspaceStore"/>.</summary>
public sealed class WorkspaceStoreWipePingTests
{
    [Fact]
    public async Task Ping_defaults_false_without_settings_row()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;
        var store = new WorkspaceStore(context, new FixedClock(DateTimeOffset.UnixEpoch));

        Assert.False(await store.GetPingEveryoneOnWipeAsync(10UL));
    }

    [Fact]
    public async Task Set_true_then_get_round_trips_and_upserts_row()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;
        var store = new WorkspaceStore(context, new FixedClock(DateTimeOffset.UnixEpoch));

        await store.SetPingEveryoneOnWipeAsync(10UL, enabled: true);

        Assert.True(await store.GetPingEveryoneOnWipeAsync(10UL));
        // The upserted row keeps the default culture.
        Assert.Equal("en", await store.GetCultureAsync(10UL));
    }

    [Fact]
    public async Task Set_preserves_existing_culture()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;
        var store = new WorkspaceStore(context, new FixedClock(DateTimeOffset.UnixEpoch));
        await store.SetCultureAsync(10UL, "fr");

        await store.SetPingEveryoneOnWipeAsync(10UL, enabled: true);

        Assert.Equal("fr", await store.GetCultureAsync(10UL));
        Assert.True(await store.GetPingEveryoneOnWipeAsync(10UL));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
