using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Switches.Modules;

/// <summary>Maps a failed actuation's reachability to a localized ephemeral reply.</summary>
internal static class SwitchActuationReply
{
    /// <summary>Returns the localized reason text for a non-Reachable actuation result.</summary>
    /// <param name="reachability">The device reachability returned by the actuation call.</param>
    /// <param name="localizer">The localizer used to resolve the string.</param>
    /// <param name="culture">The BCP-47 culture tag for the guild.</param>
    public static string Describe(DeviceReachability reachability, ILocalizer localizer, string culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var key = reachability switch
        {
            DeviceReachability.Removed => "switch.actuation.removed",
            DeviceReachability.NoPrivilege => "switch.actuation.noprivilege",
            _ => "switch.actuation.noresponse",
        };
        return localizer.Get(key, culture);
    }
}
