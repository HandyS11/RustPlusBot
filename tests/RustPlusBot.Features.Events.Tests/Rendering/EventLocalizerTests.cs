using RustPlusBot.Features.Events.Rendering;

namespace RustPlusBot.Features.Events.Tests.Rendering;

public sealed class EventLocalizerTests
{
    private static readonly EventLocalizer Sut = new(EventLocalizationCatalog.Default);

    [Fact]
    public void ReturnsFrench_WhenCultureFr() =>
        Assert.Equal("🚁 Chinook apparu en {0}", Sut.Get("event.chinook.spawned", "fr"));

    [Fact]
    public void ReturnsEnglish_WhenCultureEn() =>
        Assert.Equal("🚁 Chinook spawned at {0}", Sut.Get("event.chinook.spawned", "en"));

    [Fact]
    public void FallsBackToEnglish_WhenCultureUnknown() =>
        Assert.Equal("🚁 Chinook spawned at {0}", Sut.Get("event.chinook.spawned", "xx"));

    [Fact]
    public void ReturnsKey_WhenKeyUnknown() =>
        Assert.Equal("unknown.key", Sut.Get("unknown.key", "en"));

    [Fact]
    public void NormalizesRegion() =>
        Assert.Equal("🚁 Chinook apparu en {0}", Sut.Get("event.chinook.spawned", "fr-FR"));
}
