using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the #map channel id for a (guild, server).</summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class MapChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : CachingChannelLocator(scopeFactory, clock, WorkspaceChannelKeys.ServerMap), IMapChannelLocator;
