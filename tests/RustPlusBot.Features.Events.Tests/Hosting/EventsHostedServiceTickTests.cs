using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Events.Hosting;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Events.Tests.Hosting;

public sealed class EventsHostedServiceTickTests
{
    [Fact]
    public async Task TickOnce_publishes_a_crate_lootable_event_when_active_window_elapsed()
    {
        var clock = new TestClock();
        var options = Options.Create(new ConnectionOptions());
        var rigStore = new RigStateStore(clock, options);
        var bus = Substitute.For<IEventBus>();
        var server = Guid.NewGuid();

        rigStore.Apply(new RigStateChangedEvent(1UL, server, RigKind.Small, RigEventKind.Activated, 1f, 2f, null));
        clock.UtcNow = clock.UtcNow.Add(options.Value.RigActiveWindow);

        var service = EventsHostedServiceTestAccess.Create(bus, rigStore, clock, options);
        await service.TickOnceAsync(CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<RigStateChangedEvent>(e => e.Kind == RigEventKind.CrateLootable && e.Rig == RigKind.Small),
            Arg.Any<CancellationToken>());
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    }
}

/// <summary>Test-only factory that constructs <see cref="EventsHostedService"/> with minimal real dependencies.</summary>
internal static class EventsHostedServiceTestAccess
{
    /// <summary>Creates an <see cref="EventsHostedService"/> wired to the provided bus, rig store, clock, and options.
    /// Dependencies not exercised by <see cref="EventsHostedService.TickOnceAsync"/> are stubbed.</summary>
    /// <param name="eventBus">The event bus to publish crossings on.</param>
    /// <param name="rigStore">The rig state store to advance.</param>
    /// <param name="clock">Supplies the current time.</param>
    /// <param name="options">Supplies rig timing windows.</param>
    /// <returns>A constructed <see cref="EventsHostedService"/>.</returns>
    internal static EventsHostedService Create(
        IEventBus eventBus,
        RigStateStore rigStore,
        IClock clock,
        IOptions<ConnectionOptions> options)
    {
        var eventStateStore = new EventStateStore(clock);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var logger = NullLogger<EventsHostedService>.Instance;

        return new EventsHostedService(
            eventBus,
            relay: null!,
            eventStateStore,
            rigStore,
            clock,
            options,
            scopeFactory,
            logger);
    }
}
