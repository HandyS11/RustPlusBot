using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>Shared rig-status reply formatting for the !small/!large handlers.</summary>
internal static class RigReply
{
    /// <summary>Formats the localized rig-status reply for a rig.</summary>
    /// <param name="rigState">The rig state reader.</param>
    /// <param name="context">The command context.</param>
    /// <param name="rig">Which rig.</param>
    /// <param name="prefix">The localization key prefix ("command.small" / "command.large").</param>
    /// <param name="localizer">The reply localizer.</param>
    /// <returns>The localized reply.</returns>
    public static string For(
        IRigState rigState,
        CommandContext context,
        RigKind rig,
        string prefix,
        ILocalizer localizer)
    {
        var state = rigState.Get(context.GuildId, context.ServerId, rig);
        return state.Status switch
        {
            RigStatus.Online => localizer.Get($"{prefix}.online", context.Culture),
            RigStatus.Active => localizer.Get($"{prefix}.active", context.Culture,
                DurationFormat.Compact(state.Remaining ?? TimeSpan.Zero)),
            RigStatus.Offline => localizer.Get($"{prefix}.offline", context.Culture,
                DurationFormat.Compact(state.Remaining ?? TimeSpan.Zero)),
            _ => localizer.Get($"{prefix}.online", context.Culture),
        };
    }
}
