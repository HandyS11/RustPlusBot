using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Persistord.Testing;
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

        // Keep one private in-memory database (singleton) and give each scope its OWN context over the
        // connection it holds open — Options() reuses that connection rather than the connection string.
        var (seed, database) = TestDb.Create();
        seed.Dispose();
        services.AddSingleton(database);
        services.AddScoped(sp => new BotDbContext(
            sp.GetRequiredService<SqliteTestDatabase>().Options<BotDbContext>()));
        services.AddScoped<IFcmRegistrationStore, FcmRegistrationStore>();
        var handler = Substitute.For<IPairingHandler>();
        services.AddScoped(_ => handler);

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
            Protector = protector,
            Handler = handler,
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
        Assert.NotNull(h.Source.LastCallback);
        await h.Source.LastCallback(note, CancellationToken.None);

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

    [Fact]
    public async Task StopListener_DisposesTheRunningListener()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);
        await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        await h.Supervisor.StopListenerAsync(10UL, 99UL);

        Assert.Equal(1, h.Source.DisposeCount);
    }

#pragma warning disable S2699 // The implicit assertion is "no exception is thrown".
    [Fact]
    public async Task StopListener_WhenNotRunning_DoesNotThrow()
    {
        var source = new FakePairingSource();
        await using var h = CreateHarness(source);

        await h.Supervisor.StopListenerAsync(10UL, 12345UL);
    }
#pragma warning restore S2699

    [Fact]
    public async Task EnsureListener_for_an_owner_with_no_registration_is_rejected_without_a_listener()
    {
        var source = new FakePairingSource();
        await using var h = CreateHarness(source);

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        Assert.Equal(PairingConnectOutcome.Rejected, outcome);
        Assert.Equal(0, h.Source.CreateCount);
    }

    [Fact]
    public async Task EnsureListener_for_a_disabled_registration_is_rejected_without_a_listener()
    {
        // Disabled is how /account disconnect retires a registration. Reconnecting it on the next
        // /pair would silently undo the disconnect the owner just asked for.
        var source = new FakePairingSource();
        await using var h = CreateHarness(source);
        var id = await SeedRegistrationAsync(h.Provider, 10UL, 99UL);
        using (var scope = h.Provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>()
                .SetStatusAsync(id, FcmRegistrationStatus.Disabled);
        }

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        Assert.Equal(PairingConnectOutcome.Rejected, outcome);
        Assert.Equal(0, h.Source.CreateCount);
        Assert.Empty(h.Notifier.Notified);
    }

    [Fact]
    public async Task Credentials_that_no_longer_decrypt_expire_the_registration_and_notify_the_owner()
    {
        // A rotated data-protection key leaves the stored blob unreadable. Retrying it forever would be
        // silent: the owner has to be told to pair again.
        var source = new FakePairingSource();
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);
        h.Protector.Unprotect(Arg.Any<string>()).Throws(new CryptographicException("key not found"));

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        Assert.Equal(PairingConnectOutcome.Rejected, outcome);
        Assert.Equal(0, h.Source.CreateCount);
        Assert.Contains((10UL, 99UL), h.Notifier.Notified);
        using var scope = h.Provider.CreateScope();
        var reg = await scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>().GetAsync(10UL, 99UL);
        Assert.Equal(FcmRegistrationStatus.Expired, reg!.Status);
    }

    [Fact]
    public async Task EnsureListener_after_shutdown_starts_nothing()
    {
        // A /pair that lands while the host is shutting down must not leave an FCM listener running past
        // the point where StopAllAsync has already swept them.
        var source = new FakePairingSource();
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);
        await h.Supervisor.StopAllAsync();

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        Assert.Equal(PairingConnectOutcome.Timeout, outcome);
        Assert.Equal(0, h.Source.CreateCount);
    }

    [Fact]
    public async Task A_probe_that_never_answers_is_disposed_when_the_supervisor_stops()
    {
        // The FCM connect can hang indefinitely. Shutdown has to reclaim that listener, not leak it.
        var source = new FakePairingSource();
        source.BlockUntilCancelled();
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);

        using var caller = new CancellationTokenSource();
        var pending = h.Supervisor.EnsureListenerAsync(10UL, 99UL, caller.Token);
        await source.Connecting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        await h.Supervisor.StopAllAsync();

        Assert.Equal(1, h.Source.DisposeCount);
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_take_the_listener_down_with_it()
    {
        // One malformed push must not cost the owner every later pairing notification.
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);
        await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        h.Handler.HandleAsync(Arg.Any<ulong>(), Arg.Any<ulong>(), Arg.Any<PairingNotification>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("malformed push"));

        var note = new PairingNotification(PairingKind.Server, "S", "1.2.3.4", 28015, 7UL, "tok");
        Assert.NotNull(h.Source.LastCallback);
        await h.Source.LastCallback(note, CancellationToken.None);

        h.Handler.HandleAsync(Arg.Any<ulong>(), Arg.Any<ulong>(), Arg.Any<PairingNotification>(),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await h.Source.LastCallback(note, CancellationToken.None);

        await h.Handler.Received(2).HandleAsync(10UL, 99UL, note, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Repeated probe timeouts must back off and then STOP growing at MaxRetryDelay. An uncapped doubling
    /// walks an owner whose FCM push channel is briefly unavailable out to hours between attempts, so their
    /// pairing never recovers on its own — the delay must saturate instead.
    /// </summary>
    /// <remarks>
    /// With Initial=100ms and Max=400ms the retry intervals are 100, 200, 400, 400, 400: the 4th→5th and
    /// 5th→6th gaps are both the cap. Uncapped they would be 800 and 1600, so comparing those two gaps to
    /// each other — rather than to an absolute wall-clock budget — separates the two behaviours by 800ms
    /// while staying immune to a uniformly slow runner: scheduling jitter inflates both gaps, doubling
    /// does not.
    /// </remarks>
    [Fact]
    public async Task Retries_stop_growing_once_the_backoff_reaches_its_ceiling()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Timeout);
        source.EnqueueOutcome(PairingConnectOutcome.Timeout);
        source.EnqueueOutcome(PairingConnectOutcome.Timeout);
        source.EnqueueOutcome(PairingConnectOutcome.Timeout);
        source.EnqueueOutcome(PairingConnectOutcome.Timeout);
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source, new PairingOptions
        {
            ProbeTimeout = TimeSpan.FromSeconds(1),
            InitialRetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromMilliseconds(400),
        });
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);

        Assert.Equal(PairingConnectOutcome.Timeout, await h.Supervisor.EnsureListenerAsync(10UL, 99UL));

        await h.Source.ConnectedSignal.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var attempts = h.Source.CreateTimestamps.ToArray();
        Assert.True(attempts.Length >= 6, $"expected 6 connect attempts, saw {attempts.Length}");
        var beforeCap = Stopwatch.GetElapsedTime(attempts[3], attempts[4]);
        var atCap = Stopwatch.GetElapsedTime(attempts[4], attempts[5]);

        // Task.Delay never fires early, so both gaps are at least the cap; this pins that the backoff had
        // actually reached it rather than still ramping up.
        Assert.True(
            beforeCap >= TimeSpan.FromMilliseconds(350),
            $"attempt 4->5 should have waited the 400ms cap, waited {beforeCap.TotalMilliseconds:F0}ms");

        // The load-bearing assertion: the next gap must NOT have doubled. 250ms of slack absorbs scheduler
        // jitter; an uncapped backoff would be 800ms longer, far outside it.
        Assert.True(
            atCap <= beforeCap + TimeSpan.FromMilliseconds(250),
            $"backoff kept growing past the cap: {beforeCap.TotalMilliseconds:F0}ms then "
            + $"{atCap.TotalMilliseconds:F0}ms");
    }

    [Fact]
    public async Task A_listener_that_cannot_even_be_created_still_answers_the_caller()
    {
        // EnsureListenerAsync is awaited by the /pair command handler. If the retry loop died without
        // publishing an outcome the interaction would hang until Discord timed it out.
        var source = new FakePairingSource();
        source.FailCreation(new InvalidOperationException("the FCM credentials blob is not usable."));
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(PairingConnectOutcome.Timeout, outcome);
        Assert.Equal(1, h.Source.CreateCount);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required FakePairingSource Source { get; init; }
        public required RecordingOwnerNotifier Notifier { get; init; }
        public required PairingSupervisor Supervisor { get; init; }
        public required ICredentialProtector Protector { get; init; }
        public required IPairingHandler Handler { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Supervisor.StopAllAsync();
            await Provider.DisposeAsync();
        }
    }
}
