using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Persistence.Tests.Credentials;

public sealed class FcmRegistrationStoreTests
{
    private static ICredentialProtector PassThroughProtector()
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Protect(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        return protector;
    }

    private static IClock FixedClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
        return clock;
    }

    [Fact]
    public async Task Upsert_StoresProtectedAndActive()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var store = new FcmRegistrationStore(context, PassThroughProtector(), FixedClock());

        var id = await store.UpsertAsync(10UL, 99UL, "{\"a\":1}");

        var saved = await context.FcmRegistrations.SingleAsync();
        Assert.Equal(id, saved.Id);
        Assert.Equal("enc:{\"a\":1}", saved.ProtectedFcmCredentials);
        Assert.Equal(FcmRegistrationStatus.Active, saved.Status);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), saved.UpdatedAt);
    }

    [Fact]
    public async Task Upsert_SameOwner_RefreshesAndReactivates()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var store = new FcmRegistrationStore(context, PassThroughProtector(), FixedClock());

        var id = await store.UpsertAsync(10UL, 99UL, "old");
        await store.SetStatusAsync(id, FcmRegistrationStatus.Expired);

        var again = await store.UpsertAsync(10UL, 99UL, "new");

        Assert.Equal(id, again);
        var saved = await context.FcmRegistrations.SingleAsync();
        Assert.Equal("enc:new", saved.ProtectedFcmCredentials);
        Assert.Equal(FcmRegistrationStatus.Active, saved.Status);
    }

    [Fact]
    public async Task ListActive_ReturnsOnlyActiveAcrossGuilds()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var store = new FcmRegistrationStore(context, PassThroughProtector(), FixedClock());

        await store.UpsertAsync(10UL, 1UL, "a");
        var expiredId = await store.UpsertAsync(10UL, 2UL, "b");
        await store.SetStatusAsync(expiredId, FcmRegistrationStatus.Expired);
        await store.UpsertAsync(20UL, 3UL, "c");

        var active = await store.ListActiveAsync();

        Assert.Equal(2, active.Count);
        Assert.All(active, r => Assert.Equal(FcmRegistrationStatus.Active, r.Status));
    }

    [Fact]
    public async Task SetStatus_UpdatesStatusAndTimestamp()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var store = new FcmRegistrationStore(context, PassThroughProtector(), FixedClock());

        var id = await store.UpsertAsync(10UL, 99UL, "a");
        await store.SetStatusAsync(id, FcmRegistrationStatus.Expired);

        var saved = await context.FcmRegistrations.SingleAsync();
        Assert.Equal(FcmRegistrationStatus.Expired, saved.Status);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), saved.UpdatedAt);
    }

    [Fact]
    public async Task Get_ReturnsRegistrationForOwner_OrNull()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var store = new FcmRegistrationStore(context, PassThroughProtector(), FixedClock());

        await store.UpsertAsync(10UL, 99UL, "a");

        Assert.NotNull(await store.GetAsync(10UL, 99UL));
        Assert.Null(await store.GetAsync(10UL, 1UL));
    }
}
