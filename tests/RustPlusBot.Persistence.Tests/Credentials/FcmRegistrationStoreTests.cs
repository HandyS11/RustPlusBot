using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Persistence.Tests.Credentials;

public sealed class FcmRegistrationStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    private static ICredentialProtector PassThroughProtector()
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Protect(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        return protector;
    }

    [Fact]
    public async Task Upsert_StoresProtectedAndActive()
    {
        var (context, database) = SqliteContextFixture.Create(new FixedTimeProvider(Now));
        await using var _ = database;
        await using var __ = context;
        var store = new FcmRegistrationStore(context, PassThroughProtector());

        var id = await store.UpsertAsync(10UL, 99UL, "{\"a\":1}");

        var saved = await context.FcmRegistrations.SingleAsync();
        Assert.Equal(id, saved.Id);
        Assert.Equal("enc:{\"a\":1}", saved.ProtectedFcmCredentials);
        Assert.Equal(FcmRegistrationStatus.Active, saved.Status);
        Assert.Equal(Now, saved.UpdatedAt);
    }

    [Fact]
    public async Task Upsert_SameOwner_RefreshesAndReactivates()
    {
        var (context, database) = SqliteContextFixture.Create(new FixedTimeProvider(Now));
        await using var _ = database;
        await using var __ = context;
        var store = new FcmRegistrationStore(context, PassThroughProtector());

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
        var (context, database) = SqliteContextFixture.Create(new FixedTimeProvider(Now));
        await using var _ = database;
        await using var __ = context;
        var store = new FcmRegistrationStore(context, PassThroughProtector());

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
        var (context, database) = SqliteContextFixture.Create(new FixedTimeProvider(Now));
        await using var _ = database;
        await using var __ = context;
        var store = new FcmRegistrationStore(context, PassThroughProtector());

        var id = await store.UpsertAsync(10UL, 99UL, "a");
        await store.SetStatusAsync(id, FcmRegistrationStatus.Expired);

        var saved = await context.FcmRegistrations.SingleAsync();
        Assert.Equal(FcmRegistrationStatus.Expired, saved.Status);
        Assert.Equal(Now, saved.UpdatedAt);
    }

    [Fact]
    public async Task Get_ReturnsRegistrationForOwner_OrNull()
    {
        var (context, database) = SqliteContextFixture.Create(new FixedTimeProvider(Now));
        await using var _ = database;
        await using var __ = context;
        var store = new FcmRegistrationStore(context, PassThroughProtector());

        await store.UpsertAsync(10UL, 99UL, "a");

        Assert.NotNull(await store.GetAsync(10UL, 99UL));
        Assert.Null(await store.GetAsync(10UL, 1UL));
    }
}
