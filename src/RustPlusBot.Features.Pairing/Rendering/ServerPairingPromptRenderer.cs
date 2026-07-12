using Discord;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Pairing.Rendering;

/// <summary>Renders the "New server detected — Add it?" prompt and the post-accept confirmation. Pure.</summary>
/// <param name="localizer">The shared localizer.</param>
internal sealed class ServerPairingPromptRenderer(ILocalizer localizer)
{
    /// <summary>Renders the transient server-pairing prompt with Accept/Dismiss buttons.</summary>
    /// <param name="serverName">The server's display name from the pairing.</param>
    /// <param name="ip">The server host or ip.</param>
    /// <param name="port">The Rust+ app port.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The prompt embed and Accept/Dismiss row.</returns>
    public (Embed Embed, MessageComponent Components) RenderPrompt(
        string serverName,
        string ip,
        int port,
        string culture)
    {
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("server.prompt.title", culture))
            .WithDescription(localizer.Get("server.prompt.body", culture, serverName, ip, port))
            .Build();

        var tail = ServerPairingComponentIds.Tail(ip, port);
        var components = new ComponentBuilder()
            .WithButton(localizer.Get("server.prompt.accept", culture), ServerPairingComponentIds.AcceptPrefix + tail,
                ButtonStyle.Success)
            .WithButton(localizer.Get("server.prompt.dismiss", culture),
                ServerPairingComponentIds.DismissPrefix + tail, ButtonStyle.Secondary)
            .Build();

        return (embed, components);
    }

    /// <summary>Renders the buttonless confirmation the prompt is edited into after Accept.</summary>
    /// <param name="serverName">The server's display name.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The confirmation embed and an empty component row.</returns>
    public (Embed Embed, MessageComponent Components) RenderAdded(string serverName, string culture)
    {
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("server.prompt.added.title", culture))
            .WithDescription(localizer.Get("server.prompt.added.body", culture, serverName))
            .Build();

        return (embed, new ComponentBuilder().Build());
    }
}
