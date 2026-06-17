namespace RustPlusBot.Features.Events.State;

/// <summary>A point-in-time read of an oil rig's status and time remaining in the current phase.</summary>
/// <param name="Status">The current status.</param>
/// <param name="Remaining">Time left in the current timed phase, or null when Online (no timer).</param>
public sealed record RigState(RigStatus Status, TimeSpan? Remaining);
