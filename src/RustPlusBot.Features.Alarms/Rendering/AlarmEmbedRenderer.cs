using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Alarms.Rendering;

/// <summary>Renders a Smart Alarm as a Discord embed + control row, and the pairing-prompt embed + row. Pure.</summary>
/// <param name="localizer">The alarm localizer.</param>
/// <param name="clock">The clock used to compute relative trigger times.</param>
internal sealed class AlarmEmbedRenderer(ILocalizer localizer, IClock clock)
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
        if (unreachable)
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

        var triggered = alarm.LastTriggeredUtc is { } t
            ? localizer.Get("alarm.embed.lasttriggered", culture, CompactDuration(clock.UtcNow - t))
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
                alarm.PingEveryone ? ButtonStyle.Success : ButtonStyle.Secondary, disabled: unreachable)
            .WithButton(localizer.Get(relayKey, culture), AlarmComponentIds.RelayTogglePrefix + tail,
                alarm.RelayToTeamChat ? ButtonStyle.Success : ButtonStyle.Secondary, disabled: unreachable)
            .WithButton(localizer.Get("alarm.button.rename", culture), AlarmComponentIds.RenamePrefix + tail,
                ButtonStyle.Secondary, disabled: unreachable)
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

    /// <summary>Formats a duration compactly: "5m", "2h 10m", "3d 4h", "&lt;1m".</summary>
    /// <param name="span">The duration to format.</param>
    /// <returns>A compact human-readable duration string.</returns>
    private static string CompactDuration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours}h");
        }

        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes}m");
        }

        if (span.TotalMinutes >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m");
        }

        return "<1m";
    }
}
