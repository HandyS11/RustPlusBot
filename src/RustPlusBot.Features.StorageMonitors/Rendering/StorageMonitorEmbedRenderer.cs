using System.Globalization;
using System.Text;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.StorageMonitors.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.StorageMonitors.Rendering;

/// <summary>Renders a Smart Storage Monitor as a Discord embed + control row, and the pairing prompt. Pure.</summary>
/// <param name="localizer">The shared localizer.</param>
/// <param name="names">Resolves item ids to display names.</param>
internal sealed class StorageMonitorEmbedRenderer(ILocalizer localizer, IItemNameResolver names)
{
    /// <summary>Renders the monitor embed and its Refresh/Rename buttons. <paramref name="contents"/> null ⇒ unreachable.</summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="contents">The current contents, or null when unreachable.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The embed and the component row.</returns>
    public (Embed Embed, MessageComponent Components) RenderMonitor(
        SmartStorageMonitor monitor,
        StorageContentsSnapshot? contents,
        string culture)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var unreachable = contents is null;

        var description = new StringBuilder();
        if (unreachable)
        {
            description.Append(localizer.Get("storage.status.unreachable", culture));
        }
        else
        {
            description.AppendLine(localizer.Get(TypeKey(contents!.Capacity), culture));
            AppendProtection(description, contents, culture);
            AppendContents(description, contents, culture);
        }

        var embed = new EmbedBuilder()
            .WithTitle(monitor.Name)
            .WithDescription(description.ToString())
            .WithFooter(localizer.Get("storage.embed.footer", culture, monitor.EntityId))
            .Build();

        var tail = $"{monitor.ServerId}:{monitor.EntityId}";
        var components = new ComponentBuilder()
            .WithButton(localizer.Get("storage.button.refresh", culture),
                StorageMonitorComponentIds.RefreshPrefix + tail, ButtonStyle.Primary, disabled: unreachable)
            .WithButton(localizer.Get("storage.button.rename", culture),
                StorageMonitorComponentIds.RenamePrefix + tail, ButtonStyle.Secondary, disabled: unreachable)
            .Build();

        return (embed, components);
    }

    /// <summary>Renders the transient "New storage monitor detected — Add it?" prompt.</summary>
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
            .WithTitle(localizer.Get("storage.prompt.title", culture))
            .WithDescription(localizer.Get("storage.prompt.body", culture, defaultName))
            .Build();

        var tail = $"{serverId}:{entityId}";
        var components = new ComponentBuilder()
            .WithButton(localizer.Get("storage.prompt.accept", culture),
                StorageMonitorComponentIds.AcceptPrefix + tail, ButtonStyle.Success)
            .WithButton(localizer.Get("storage.prompt.dismiss", culture),
                StorageMonitorComponentIds.DismissPrefix + tail, ButtonStyle.Secondary)
            .Build();

        return (embed, components);
    }

    private static string TypeKey(int? capacity) => capacity switch
    {
        24 => "storage.type.toolcupboard",
        48 => "storage.type.largebox",
        12 => "storage.type.smallbox",
        _ => "storage.type.unknown",
    };

    private static string FormatRemaining(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours}h");
        }

        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes}m");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m");
    }

    private void AppendProtection(StringBuilder sb, StorageContentsSnapshot contents, string culture)
    {
        // Protection is only meaningful for a Tool Cupboard (capacity 24).
        if (contents.Capacity != 24)
        {
            return;
        }

        if (contents is { HasProtection: true, ProtectionExpiry: { } expiry })
        {
            var remaining = expiry - DateTimeOffset.UtcNow;
            sb.AppendLine(localizer.Get("storage.protection.on", culture,
                FormatRemaining(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining)));
        }
        else
        {
            sb.AppendLine(localizer.Get("storage.protection.off", culture));
        }
    }

    private void AppendContents(StringBuilder sb, StorageContentsSnapshot contents, string culture)
    {
        var capacityText = contents.Capacity?.ToString(CultureInfo.InvariantCulture) ?? "?";
        sb.AppendLine(localizer.Get("storage.slots", culture, contents.Items.Count, capacityText));

        if (contents.Items.Count == 0)
        {
            sb.AppendLine(localizer.Get("storage.contents.empty", culture));
            return;
        }

        foreach (var item in contents.Items.OrderByDescending(i => i.Quantity))
        {
            var name = names.Resolve(item.ItemId);
            if (item.IsBlueprint)
            {
                name = localizer.Get("storage.contents.bp", culture, name);
            }

            sb.AppendLine(localizer.Get("storage.contents.line", culture, name, item.Quantity));
        }
    }
}
