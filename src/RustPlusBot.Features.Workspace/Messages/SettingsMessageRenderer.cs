using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders the global #settings message with the language selector and the wipe-ping toggle.</summary>
/// <param name="store">Reads the guild's wipe-ping flag.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class SettingsMessageRenderer(IWorkspaceStore store, ILocalizer localizer) : IMessageRenderer
{
    /// <summary>Custom id of the language select menu, handled by the settings component module.</summary>
    public const string LanguageSelectId = "workspace:settings:culture";

    /// <summary>Custom id of the @everyone-on-wipe toggle button, handled by the settings component module.</summary>
    public const string WipePingButtonId = "workspace:settings:wipeping";

    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.SettingsMain;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ping = await store.GetPingEveryoneOnWipeAsync(context.GuildId, cancellationToken).ConfigureAwait(false);

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("settings.title", context.Culture))
            .WithDescription(localizer.Get("settings.body", context.Culture))
            .WithColor(Color.DarkGrey)
            .Build();

        var menu = new SelectMenuBuilder()
            .WithCustomId(LanguageSelectId)
            .WithPlaceholder(localizer.Get("settings.language.label", context.Culture))
            .AddOption("English", "en")
            .AddOption("Français", "fr");
        var wipePing = new ButtonBuilder()
            .WithCustomId(WipePingButtonId)
            .WithLabel(localizer.Get(ping ? "settings.wipeping.on" : "settings.wipeping.off", context.Culture))
            .WithStyle(ping ? ButtonStyle.Success : ButtonStyle.Secondary);
        var components = new ComponentBuilder()
            .WithSelectMenu(menu)
            .WithButton(wipePing, row: 1)
            .Build();

        return new MessagePayload(null, embed, components);
    }
}
