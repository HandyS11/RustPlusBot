using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class CctvFormatterTests
{
    [Fact]
    public void CctvLine_RendersHeaderThenCodesInOrder()
    {
        var monument = new CctvMonument("Small Oil Rig", ["OILRIG1HELI", "OILRIG1DOCK"], false);
        var line = CctvLine.Format(monument);
        Assert.Equal("Small Oil Rig CCTV:\nOILRIG1HELI\nOILRIG1DOCK", line);
    }

    [Fact]
    public void CctvLine_DoesNotAppendNote_EvenWhenDynamic()
    {
        var monument = new CctvMonument("Underwater Labs", ["LAB****"], true);
        var line = CctvLine.Format(monument);
        Assert.Equal("Underwater Labs CCTV:\nLAB****", line);
    }
}
