using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>An in-game storage-monitor broadcast: the entity id and its current contents.</summary>
/// <param name="EntityId">The in-game entity id.</param>
/// <param name="Contents">The contents snapshot carried on the broadcast.</param>
internal sealed record StorageMonitorTrigger(ulong EntityId, StorageContentsSnapshot Contents);
