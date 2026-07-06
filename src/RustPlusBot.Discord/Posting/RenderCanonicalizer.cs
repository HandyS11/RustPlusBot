using System.Globalization;
using System.Text;
using Discord;

namespace RustPlusBot.Discord.Posting;

/// <summary>
///     Flattens an embed + components render into a deterministic canonical string so identical
///     re-renders can be detected and skipped before hitting Discord's per-channel PATCH bucket.
///     Values are length-prefixed to make adjacent user-controlled strings collision-proof.
/// </summary>
/// <remarks>
///     This repo's renderers only ever emit <see cref="ActionRowComponent" />s of buttons and select
///     menus, which are canonicalized field-by-field. Any other component kind (e.g. Components-V2
///     layouts, text inputs) is canonicalized shallowly: only its <see cref="ComponentType" /> and,
///     when it implements <see cref="IInteractableComponent" />, its custom id are captured. Add a
///     dedicated case in <c>AppendComponent</c> if this repo ever sends a richer unmodeled kind.
/// </remarks>
public static class RenderCanonicalizer
{
    /// <summary>Builds the canonical string for a render.</summary>
    /// <param name="embed">The embed about to be sent, or null.</param>
    /// <param name="components">The message components about to be sent, or null.</param>
    /// <returns>A deterministic string that is equal iff the visible render is equal.</returns>
    public static string Canonicalize(Embed? embed, MessageComponent? components)
    {
        var sb = new StringBuilder();
        if (embed is not null)
        {
            AppendEmbed(sb, embed);
        }

        if (components is not null)
        {
            AppendComponents(sb, components);
        }

        return sb.ToString();
    }

    private static void AppendEmbed(StringBuilder sb, Embed embed)
    {
        Append(sb, "title", embed.Title);
        Append(sb, "description", embed.Description);
        Append(sb, "url", embed.Url);
        Append(sb, "color", embed.Color?.RawValue.ToString(CultureInfo.InvariantCulture));
        Append(sb, "timestamp", embed.Timestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        Append(sb, "author.name", embed.Author?.Name);
        Append(sb, "author.url", embed.Author?.Url);
        Append(sb, "author.icon", embed.Author?.IconUrl);
        Append(sb, "footer.text", embed.Footer?.Text);
        Append(sb, "footer.icon", embed.Footer?.IconUrl);
        Append(sb, "thumbnail", embed.Thumbnail?.Url);
        Append(sb, "image", embed.Image?.Url);
        foreach (var field in embed.Fields)
        {
            Append(sb, "field.name", field.Name);
            Append(sb, "field.value", field.Value);
            Append(sb, "field.inline", field.Inline ? "1" : "0");
        }
    }

    private static void AppendComponents(StringBuilder sb, MessageComponent components)
    {
        foreach (var component in components.Components)
        {
            if (component is ActionRowComponent row)
            {
                Append(sb, "row", null);
                foreach (var child in row.Components)
                {
                    AppendComponent(sb, child);
                }
            }
            else
            {
                // Top-level non-ActionRow component (e.g. a Components-V2 layout piece): fall back
                // to the same shallow append used for unmodeled component kinds.
                AppendComponent(sb, component);
            }
        }
    }

    private static void AppendComponent(StringBuilder sb, IMessageComponent component)
    {
        switch (component)
        {
            case ButtonComponent button:
                Append(sb, "button.id", button.CustomId);
                Append(sb, "button.label", button.Label);
                Append(sb, "button.style", ((int)button.Style).ToString(CultureInfo.InvariantCulture));
                Append(sb, "button.url", button.Url);
                Append(sb, "button.disabled", button.IsDisabled ? "1" : "0");
                Append(sb, "button.emote", button.Emote?.ToString());
                break;
            case SelectMenuComponent menu:
                Append(sb, "menu.id", menu.CustomId);
                Append(sb, "menu.placeholder", menu.Placeholder);
                Append(sb, "menu.min", menu.MinValues.ToString(CultureInfo.InvariantCulture));
                Append(sb, "menu.max", menu.MaxValues.ToString(CultureInfo.InvariantCulture));
                Append(sb, "menu.disabled", menu.IsDisabled ? "1" : "0");
                foreach (var option in menu.Options)
                {
                    Append(sb, "option.label", option.Label);
                    Append(sb, "option.value", option.Value);
                    Append(sb, "option.description", option.Description);
                    Append(sb, "option.emote", option.Emote?.ToString());
                    Append(sb, "option.default", option.IsDefault == true ? "1" : "0");
                }

                break;
            default:
                // Unmodeled component kind: type + custom id (when the component exposes one) keeps
                // the canonical string honest without building a per-kind serializer for kinds this
                // repo never sends (see the class remarks for the accepted depth).
                Append(sb, "component.type", component.Type.ToString());
                Append(sb, "component.id", (component as IInteractableComponent)?.CustomId);
                break;
        }
    }

    private static void Append(StringBuilder sb, string key, string? value)
    {
        sb.Append(key).Append('=');
        if (value is null)
        {
            sb.Append("<null>");
        }
        else
        {
            sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        }

        sb.Append('\n');
    }
}
