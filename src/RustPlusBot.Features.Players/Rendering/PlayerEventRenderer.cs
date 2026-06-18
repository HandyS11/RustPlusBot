using Discord;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Formatting;

namespace RustPlusBot.Features.Players.Rendering;

/// <summary>Renders one <see cref="PlayerTransition"/> as a Discord embed or an in-game line.</summary>
/// <param name="localizer">The reply localizer.</param>
internal sealed class PlayerEventRenderer(IPlayerLocalizer localizer)
{
    /// <summary>Renders the transition for a guild culture as a Discord embed.</summary>
    /// <param name="transition">The player transition to render.</param>
    /// <param name="dims">The map dimensions, or null if unavailable.</param>
    /// <param name="culture">The BCP-47 culture tag.</param>
    public Embed Render(PlayerTransition transition, MapDimensions? dims, string culture)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return new EmbedBuilder()
            .WithAuthor(localizer.Get("player.title", culture))
            .WithDescription(Describe(transition, dims, culture, suffix: string.Empty))
            .WithCurrentTimestamp()
            .Build();
    }

    /// <summary>Renders the in-game team-chat line for the transition.</summary>
    /// <param name="transition">The player transition to render.</param>
    /// <param name="dims">The map dimensions, or null if unavailable.</param>
    /// <param name="culture">The BCP-47 culture tag.</param>
    public string RenderLine(PlayerTransition transition, MapDimensions? dims, string culture)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return Describe(transition, dims, culture, suffix: ".line");
    }

    private string Describe(PlayerTransition t, MapDimensions? dims, string culture, string suffix)
    {
        switch (t.Kind)
        {
            case PlayerTransitionKind.Connect:
                return localizer.Get("player.connect" + suffix, culture, t.Name);
            case PlayerTransitionKind.Disconnect:
                return localizer.Get("player.disconnect" + suffix, culture, t.Name);
            case PlayerTransitionKind.Respawn:
                return localizer.Get("player.respawn" + suffix, culture, t.Name, Grid(t, dims));
            case PlayerTransitionKind.ReturnedFromAfk:
                return localizer.Get("player.afk.back" + suffix, culture, t.Name);
            case PlayerTransitionKind.BecameAfk:
                return localizer.Get("player.afk" + suffix, culture, t.Name, Grid(t, dims));
            case PlayerTransitionKind.Death:
                return t.Location is null
                    ? localizer.Get("player.death.unknown" + suffix, culture, t.Name)
                    : localizer.Get("player.death" + suffix, culture, t.Name, Grid(t, dims));
            default:
                throw new ArgumentOutOfRangeException(nameof(t), t.Kind, "Unsupported transition kind.");
        }
    }

    private static string Grid(PlayerTransition t, MapDimensions? dims)
        => t.Location is { } loc ? GridReference.From(loc.X, loc.Y, dims) : string.Empty;
}
