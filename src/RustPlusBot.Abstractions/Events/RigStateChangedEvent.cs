using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when an oil rig crosses a lifecycle boundary (activated / crate lootable / respawned).</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
/// <param name="Rig">Which rig.</param>
/// <param name="Kind">The boundary that was crossed.</param>
/// <param name="X">The rig monument's world X coordinate.</param>
/// <param name="Y">The rig monument's world Y coordinate.</param>
/// <param name="Dimensions">Map dimensions for grid-reference rendering, or null if unavailable.</param>
public sealed record RigStateChangedEvent(
    ulong GuildId,
    Guid ServerId,
    RigKind Rig,
    RigEventKind Kind,
    float X,
    float Y,
    MapDimensions? Dimensions);
