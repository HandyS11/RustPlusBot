using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Switches.Modules;
using RustPlusBot.Features.Switches.Tests.Fakes;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Switches.Tests;

public sealed class SwitchActuationReplyTests
{
    [Theory]
    [InlineData(DeviceReachability.Removed, "switch.actuation.removed")]
    [InlineData(DeviceReachability.NoPrivilege, "switch.actuation.noprivilege")]
    [InlineData(DeviceReachability.NoResponse, "switch.actuation.noresponse")]
    public void Describe_NonReachable_ReturnsReasonText(DeviceReachability reachability, string expectedKey)
    {
        // Fake localizer returns the key it is given (same pattern as other Switches.Tests).
        var text = SwitchActuationReply.Describe(reachability, new KeyEchoLocalizer(), "en");
        Assert.Equal(expectedKey, text);
    }

    [Theory]
    [InlineData(DeviceReachability.Removed, "This switch was removed in-game.")]
    [InlineData(DeviceReachability.NoPrivilege, "No building privilege for this switch.")]
    [InlineData(DeviceReachability.NoResponse, "The switch didn't respond. Try again.")]
    public void Describe_NonReachable_ReturnsCorrectEnglishText(DeviceReachability reachability, string expectedText)
    {
        // Real localizer for final EN text verification.
        var text = SwitchActuationReply.Describe(reachability, new ResxLocalizer(), "en");
        Assert.Equal(expectedText, text);
    }
}
