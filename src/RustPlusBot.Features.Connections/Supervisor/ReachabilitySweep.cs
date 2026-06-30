using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Supervisor;

/// <summary>Pure helper: which entities' reachability changed between two poll snapshots.</summary>
internal static class ReachabilitySweep
{
    /// <summary>Entities in <paramref name="current"/> whose reachability differs from <paramref name="previous"/> (absent ⇒ Reachable).</summary>
    /// <param name="previous">The reachability snapshot from the previous poll cycle.</param>
    /// <param name="current">The reachability snapshot from the current poll cycle.</param>
    /// <returns>A list of (entityId, newReachability) pairs where the state has changed.</returns>
    public static IReadOnlyList<KeyValuePair<ulong, DeviceReachability>> Diff(
        IReadOnlyDictionary<ulong, DeviceReachability> previous,
        IReadOnlyDictionary<ulong, DeviceReachability> current)
    {
        var changes = new List<KeyValuePair<ulong, DeviceReachability>>();
        foreach (var (entityId, reachability) in current)
        {
            var before = previous.TryGetValue(entityId, out var p) ? p : DeviceReachability.Reachable;
            if (before != reachability)
            {
                changes.Add(new KeyValuePair<ulong, DeviceReachability>(entityId, reachability));
            }
        }

        return changes;
    }
}
