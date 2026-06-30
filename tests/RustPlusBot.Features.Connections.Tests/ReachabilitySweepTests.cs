using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Supervisor;

namespace RustPlusBot.Features.Connections.Tests;

/// <summary>Unit tests for <see cref="ReachabilitySweep.Diff"/>.</summary>
public sealed class ReachabilitySweepTests
{
    /// <summary>Verifies that a first-observed Removed state is reported (absent ⇒ Reachable default, so Removed is a change).</summary>
    [Fact]
    public void Diff_FirstObservedRemoved_IsReported()
    {
        var current = new Dictionary<ulong, DeviceReachability>
        {
            [1] = DeviceReachability.Removed
        };
        var changes = ReachabilitySweep.Diff(new Dictionary<ulong, DeviceReachability>(), current);
        // 1 went from (absent==Reachable default) to Removed
        Assert.Single(changes);
    }

    /// <summary>Verifies that both recoveries and new degradations are reported.</summary>
    [Fact]
    public void Diff_ReportsChangesIncludingRecovery()
    {
        var previous = new Dictionary<ulong, DeviceReachability>
        {
            [1] = DeviceReachability.Removed, [2] = DeviceReachability.Reachable,
        };
        var current = new Dictionary<ulong, DeviceReachability>
        {
            [1] = DeviceReachability.Reachable, // recovered
            [2] = DeviceReachability.NoPrivilege, // newly degraded
        };
        var changes = ReachabilitySweep.Diff(previous, current);
        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Key == 1 && c.Value == DeviceReachability.Reachable);
        Assert.Contains(changes, c => c.Key == 2 && c.Value == DeviceReachability.NoPrivilege);
    }

    /// <summary>Verifies that unchanged states produce an empty result.</summary>
    [Fact]
    public void Diff_NoChanges_ReturnsEmpty()
    {
        var same = new Dictionary<ulong, DeviceReachability>
        {
            [1] = DeviceReachability.Reachable
        };
        Assert.Empty(ReachabilitySweep.Diff(same, new Dictionary<ulong, DeviceReachability>(same)));
    }
}
