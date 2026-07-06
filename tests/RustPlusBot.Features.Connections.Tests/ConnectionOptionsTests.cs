namespace RustPlusBot.Features.Connections.Tests;

/// <summary>Unit tests for <see cref="ConnectionOptions"/> default values.</summary>
public sealed class ConnectionOptionsTests
{
    /// <summary>Verifies that all defaults match the 2a-ii design specification.</summary>
    [Fact]
    public void Defaults_match_the_2a_ii_design()
    {
        var o = new ConnectionOptions();

        Assert.Equal(TimeSpan.FromSeconds(5), o.MarkerPollInterval);
        Assert.Equal(TimeSpan.FromSeconds(2), o.MarkerPollFastInterval);
        Assert.Equal(150f, o.RigRadius);
        Assert.Equal(TimeSpan.FromMinutes(15), o.RigActiveWindow);
        Assert.Equal(TimeSpan.FromMinutes(15), o.RigOfflineWindow);
        Assert.Equal(TimeSpan.FromSeconds(30), o.RigTickInterval);
    }

    /// <summary>Verifies that the reachability poll interval defaults to 5 minutes.</summary>
    [Fact]
    public void ReachabilityPollInterval_DefaultIs5Minutes()
    {
        var o = new ConnectionOptions();
        Assert.Equal(TimeSpan.FromMinutes(5), o.ReachabilityPollInterval);
    }
}
