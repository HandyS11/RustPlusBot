using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders the global #settings message with the language selector.</summary>
/// <param name="localizer">String resolution.</param>
internal sealed class SettingsMessageRenderer(ILocalizer localizer) : IMessageRenderer
{
    /// <summary>Custom id of the language select menu, handled by the settings component module.</summary>
    public const string LanguageSelectId = "workspace:settings:culture";

    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.SettingsMain;

    /// <inheritdoc />
    public ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
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
        var components = new ComponentBuilder().WithSelectMenu(menu).Build();

        return ValueTask.FromResult(new MessagePayload(null, embed, components));
    }
}
