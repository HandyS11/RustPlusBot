using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Switches.Rendering;

/// <summary>Renders a Smart Switch as a Discord embed + control row, and the pairing-prompt embed + row. Pure.</summary>
/// <param name="localizer">The switch localizer.</param>
internal sealed class SwitchEmbedRenderer(ILocalizer localizer)
{
    /// <summary>Renders the switch embed and its control buttons. <paramref name="isActive"/> null ⇒ unreachable.</summary>
    /// <param name="sw">The switch.</param>
    /// <param name="isActive">On (true), off (false), or unreachable (null).</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The embed and the component rows.</returns>
    public (Embed Embed, MessageComponent Components) RenderSwitch(SmartSwitch sw, bool? isActive, string culture)
    {
        ArgumentNullException.ThrowIfNull(sw);
        var reason = sw.Reachability;
        var blocked = reason is DeviceReachability.Removed or DeviceReachability.NoPrivilege;
        var serverDown = reason == DeviceReachability.Reachable && isActive is null;
        var statusKey = reason switch
        {
            DeviceReachability.Removed => "switch.status.removed",
            DeviceReachability.NoPrivilege => "switch.status.noprivilege",
            DeviceReachability.NoResponse => "switch.status.noresponse",
            _ => isActive switch
            {
                true => "switch.status.on",
                false => "switch.status.off",
                null => "switch.status.unreachable",
            },
        };
        var controlsDisabled = blocked || serverDown;

        var embed = new EmbedBuilder()
            .WithTitle(sw.Name)
            .WithDescription(localizer.Get(statusKey, culture))
            .WithFooter(localizer.Get("switch.embed.footer", culture, sw.EntityId))
            .Build();

        var tail = $"{sw.ServerId}:{sw.EntityId}";
        var components = new ComponentBuilder()
            .WithButton(localizer.Get("switch.button.on", culture), SwitchComponentIds.OnPrefix + tail,
                ButtonStyle.Success, disabled: controlsDisabled || isActive == true)
            .WithButton(localizer.Get("switch.button.off", culture), SwitchComponentIds.OffPrefix + tail,
                ButtonStyle.Secondary, disabled: controlsDisabled || isActive == false)
            .WithButton(localizer.Get("switch.button.strobe", culture), SwitchComponentIds.StrobePrefix + tail,
                ButtonStyle.Primary, disabled: controlsDisabled)
            .WithButton(localizer.Get("switch.button.rename", culture), SwitchComponentIds.RenamePrefix + tail,
                ButtonStyle.Secondary, disabled: controlsDisabled)
            .Build();

        return (embed, components);
    }

    /// <summary>Renders the transient "New switch detected — Add it?" prompt.</summary>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The entity id.</param>
    /// <param name="defaultName">The generated default name.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The prompt embed and Accept/Dismiss row.</returns>
    public (Embed Embed, MessageComponent Components) RenderPrompt(
        Guid serverId,
        ulong entityId,
        string defaultName,
        string culture)
    {
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("switch.prompt.title", culture))
            .WithDescription(localizer.Get("switch.prompt.body", culture, defaultName))
            .Build();

        var tail = $"{serverId}:{entityId}";
        var components = new ComponentBuilder()
            .WithButton(localizer.Get("switch.prompt.accept", culture), SwitchComponentIds.AcceptPrefix + tail,
                ButtonStyle.Success)
            .WithButton(localizer.Get("switch.prompt.dismiss", culture), SwitchComponentIds.DismissPrefix + tail,
                ButtonStyle.Secondary)
            .Build();

        return (embed, components);
    }
}
