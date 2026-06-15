using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Notifications;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Features.Pairing.Tests.Fakes;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class PairingSupervisorTests
{
    private static Harness CreateHarness(FakePairingSource source, PairingOptions? options = null)
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Protect(Arg.Any<string>()).Returns(c => c.Arg<string>());
        protector.Unprotect(Arg.Any<string>()).Returns(c => c.Arg<string>());

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton(protector);
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // Keep one open in-memory SQLite connection (singleton) and give each scope its OWN context.
        var (seed, connection) = TestDb.Create();
        seed.Dispose();
        services.AddSingleton(connection);
        services.AddScoped(sp => new BotDbContext(
            new DbContextOptionsBuilder<BotDbContext>().UseSqlite(sp.GetRequiredService<SqliteConnection>()).Options));
        services.AddScoped<IFcmRegistrationStore, FcmRegistrationStore>();
        services.AddScoped<IPairingHandler>(_ => Substitute.For<IPairingHandler>());

        var notifier = new RecordingOwnerNotifier();
        services.AddSingleton<IOwnerNotifier>(notifier);
        services.AddSingleton<IPairingSource>(source);
        services.AddSingleton(Options.Create(options ?? new PairingOptions
        {
            ProbeTimeout = TimeSpan.FromSeconds(1),
            InitialRetryDelay = TimeSpan.FromMilliseconds(5),
            MaxRetryDelay = TimeSpan.FromMilliseconds(20),
        }));
        services.AddSingleton<PairingSupervisor>();

        var provider = services.BuildServiceProvider();
        return new Harness
        {
            Provider = provider,
            Source = source,
            Notifier = notifier,
            Supervisor = provider.GetRequiredService<PairingSupervisor>(),
        };
    }

    private static async Task<Guid> SeedRegistrationAsync(ServiceProvider provider, ulong guild, ulong owner)
    {
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>();
        return await store.UpsertAsync(guild, owner, "{}");
    }

    [Fact]
    public async Task EnsureListener_Connected_ReturnsConnectedAndStaysActive()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        Assert.Equal(PairingConnectOutcome.Connected, outcome);
        Assert.Empty(h.Notifier.Notified);
        using var scope = h.Provider.CreateScope();
        var reg = await scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>().GetAsync(10UL, 99UL);
        Assert.Equal(FcmRegistrationStatus.Active, reg!.Status);
    }

    [Fact]
    public async Task EnsureListener_Rejected_MarksExpiredAndNotifies()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Rejected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        Assert.Equal(PairingConnectOutcome.Rejected, outcome);
        using var scope = h.Provider.CreateScope();
        var reg = await scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>().GetAsync(10UL, 99UL);
        Assert.Equal(FcmRegistrationStatus.Expired, reg!.Status);
        Assert.Contains((10UL, 99UL), h.Notifier.Notified);
    }

    [Fact]
    public async Task EnsureListener_TimeoutThenConnected_RetriesAndStaysActive()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Timeout);
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL);
        Assert.Equal(PairingConnectOutcome.Timeout, outcome);

        await h.Source.ConnectedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(h.Source.CreateCount >= 2);
        using var scope = h.Provider.CreateScope();
        var reg = await scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>().GetAsync(10UL, 99UL);
        Assert.Equal(FcmRegistrationStatus.Active, reg!.Status);
    }

    [Fact]
    public async Task ConnectedListener_DeliversNotificationWithoutFaulting()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);
        await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        var note = new PairingNotification(PairingKind.Server, "S", "1.2.3.4", 28015, 7UL, "tok");
        await h.Source.LastCallback!(note, CancellationToken.None);

        using var scope = h.Provider.CreateScope();
        var reg = await scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>().GetAsync(10UL, 99UL);
        Assert.Equal(FcmRegistrationStatus.Active, reg!.Status);
    }

    [Fact]
    public async Task StartAllActive_StartsAListenerPerActiveRegistration()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 1UL);
        await SeedRegistrationAsync(h.Provider, 10UL, 2UL);

        await h.Supervisor.StartAllActiveAsync();

        Assert.Equal(2, h.Source.CreateCount);
    }

    [Fact]
    public async Task EnsureListener_CalledTwiceForSameOwner_RestartsListener()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);

        await h.Supervisor.EnsureListenerAsync(10UL, 99UL);
        await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        Assert.Equal(2, h.Source.CreateCount);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required FakePairingSource Source { get; init; }
        public required RecordingOwnerNotifier Notifier { get; init; }
        public required PairingSupervisor Supervisor { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Supervisor.StopAllAsync();
            await Provider.DisposeAsync();
        }
    }
}
