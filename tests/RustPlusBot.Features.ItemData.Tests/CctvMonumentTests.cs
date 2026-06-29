using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class CctvMonumentTests
{
    [Fact]
    public void CctvMonument_ExposesNameCodesAndDynamic()
    {
        var monument = new CctvMonument("Dome", ["DOME1", "DOMETOP"], false);
        Assert.Equal("Dome", monument.Name);
        Assert.Equal(2, monument.Codes.Count);
        Assert.Equal("DOME1", monument.Codes[0]);
        Assert.False(monument.Dynamic);
    }
}
