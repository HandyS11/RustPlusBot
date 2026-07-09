namespace RustPlusBot.Features.Map.Tests;

public sealed class MapOptionsTests
{
    [Fact]
    public void Defaults_are_sane()
    {
        var options = new MapOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), options.MapRefreshInterval);
        Assert.NotNull(options.RustMaps);
        Assert.Equal(TimeSpan.FromSeconds(20), options.RustMaps.GenerationPollInterval);
    }
}
