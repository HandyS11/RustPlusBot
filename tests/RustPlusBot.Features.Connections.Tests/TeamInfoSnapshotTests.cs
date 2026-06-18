using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamInfoSnapshotTests
{
    [Fact]
    public void DeathNote_defaults_to_null()
    {
        var snap = new TeamInfoSnapshot(123UL, []);
        Assert.Null(snap.DeathNote);
    }

    [Fact]
    public void DeathNote_carries_coordinate_when_set()
    {
        var snap = new TeamInfoSnapshot(123UL, [], (100f, 200f));
        Assert.Equal((100f, 200f), snap.DeathNote);
    }
}
