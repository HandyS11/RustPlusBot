namespace RustPlusBot.Features.Connections.Listening;

/// <summary>An in-game device-state broadcast: the entity id and its new on/off state.</summary>
/// <param name="EntityId">The in-game entity id.</param>
/// <param name="IsActive">The new on/off state carried on the broadcast.</param>
internal sealed record SmartDeviceTrigger(ulong EntityId, bool IsActive);
