using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Alarms.Rendering;

/// <summary>Renders a Smart Alarm as a Discord embed + control row, and the pairing-prompt embed + row. Pure.</summary>
/// <param name="localizer">The alarm localizer.</param>
internal sealed class AlarmEmbedRenderer(ILocalizer localizer)
{
    /// <summary>Renders the alarm embed and its control buttons.</summary>
    /// <param name="alarm">The alarm to render.</param>
    /// <param name="unreachable">When true the alarm entity is currently unreachable.</param>
    /// <param name="culture">The guild culture (BCP-47 primary tag).</param>
    /// <returns>The embed and the component rows.</returns>
    public (Embed Embed, MessageComponent Components) RenderAlarm(SmartAlarm alarm, bool unreachable, string culture)
    {
        ArgumentNullException.ThrowIfNull(alarm);

        string statusKey;
#pragma warning disable IDE0045 // Collapsing to a nested ternary trips RCS1238/S3358 (nested conditional); the if/else chain is intentional.
        if (alarm.Reachability == DeviceReachability.Removed)
        {
            statusKey = "alarm.status.removed";
        }
        else if (alarm.Reachability == DeviceReachability.NoPrivilege)
        {
            statusKey = "alarm.status.noprivilege";
        }
        else if (alarm.Reachability == DeviceReachability.NoResponse)
        {
            statusKey = "alarm.status.noresponse";
        }
        else if (unreachable)
        {
            statusKey = "alarm.status.unreachable";
        }
        else if (alarm.LastIsActive)
        {
            statusKey = "alarm.status.active";
        }
        else
        {
            statusKey = "alarm.status.armed";
        }
#pragma warning restore IDE0045

        // Removed and NoPrivilege disable controls (device is gone or locked); NoResponse leaves them enabled
        // so the user can still interact while the device is temporarily unresponsive.
        var blocked = alarm.Reachability is DeviceReachability.Removed or DeviceReachability.NoPrivilege;
        var disableButtons = unreachable || blocked;

        // <t:unix:R> is Discord's native relative timestamp — clients render "x minutes ago" and keep
        // it updating live, so the embed never shows a stale duration between re-renders.
        var triggered = alarm.LastTriggeredUtc is { } t
            ? localizer.Get("alarm.embed.lasttriggered", culture,
                string.Create(CultureInfo.InvariantCulture, $"<t:{t.ToUnixTimeSeconds()}:R>"))
            : localizer.Get("alarm.embed.nevertriggered", culture);

        var embed = new EmbedBuilder()
            .WithTitle(alarm.Name)
            .WithDescription($"{localizer.Get(statusKey, culture)}\n{triggered}")
            .WithFooter(localizer.Get("alarm.embed.footer", culture, alarm.EntityId))
            .Build();

        var tail = $"{alarm.ServerId}:{alarm.EntityId}";
        var pingKey = alarm.PingEveryone ? "alarm.button.ping.on" : "alarm.button.ping.off";
        var relayKey = alarm.RelayToTeamChat ? "alarm.button.relay.on" : "alarm.button.relay.off";
        var components = new ComponentBuilder()
            .WithButton(localizer.Get(pingKey, culture), AlarmComponentIds.PingTogglePrefix + tail,
                alarm.PingEveryone ? ButtonStyle.Success : ButtonStyle.Secondary, disabled: disableButtons)
            .WithButton(localizer.Get(relayKey, culture), AlarmComponentIds.RelayTogglePrefix + tail,
                alarm.RelayToTeamChat ? ButtonStyle.Success : ButtonStyle.Secondary, disabled: disableButtons)
            .WithButton(localizer.Get("alarm.button.refresh", culture), AlarmComponentIds.RefreshPrefix + tail,
                ButtonStyle.Primary, disabled: disableButtons)
            .WithButton(localizer.Get("alarm.button.rename", culture), AlarmComponentIds.RenamePrefix + tail,
                ButtonStyle.Secondary, disabled: disableButtons)
            .Build();

        return (embed, components);
    }

    /// <summary>Renders the transient "New alarm detected — Add it?" prompt.</summary>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The entity id.</param>
    /// <param name="defaultName">The generated default name.</param>
    /// <param name="culture">The guild culture (BCP-47 primary tag).</param>
    /// <returns>The prompt embed and Accept/Dismiss row.</returns>
    public (Embed Embed, MessageComponent Components) RenderPrompt(
        Guid serverId,
        ulong entityId,
        string defaultName,
        string culture)
    {
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("alarm.prompt.title", culture))
            .WithDescription(localizer.Get("alarm.prompt.body", culture, defaultName))
            .Build();

        var tail = $"{serverId}:{entityId}";
        var components = new ComponentBuilder()
            .WithButton(localizer.Get("alarm.prompt.accept", culture), AlarmComponentIds.AcceptPrefix + tail,
                ButtonStyle.Success)
            .WithButton(localizer.Get("alarm.prompt.dismiss", culture), AlarmComponentIds.DismissPrefix + tail,
                ButtonStyle.Secondary)
            .Build();

        return (embed, components);
    }
}
