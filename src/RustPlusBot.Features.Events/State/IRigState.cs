using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Events.State;

/// <summary>Reads the current inferred oil-rig status for a server.</summary>
public interface IRigState
{
    /// <summary>Gets the current status + time remaining for one rig. An untracked rig reads as Online.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="rig">Which rig.</param>
    /// <returns>The current rig state.</returns>
    RigState Get(ulong guildId, Guid serverId, RigKind rig);
}
