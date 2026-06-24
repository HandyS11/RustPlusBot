using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders the global #information status/help embed.</summary>
/// <param name="servers">Used for the registered-server count.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class InformationMessageRenderer(IServerService servers, ILocalizer localizer) : IMessageRenderer
{
    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.InformationMain;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var count = (await servers.ListAsync(context.GuildId, cancellationToken).ConfigureAwait(false)).Count;
        var description = localizer.Get("information.body", context.Culture)
                          + "\n\n"
                          + localizer.Get("information.servers", context.Culture, count);
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("information.title", context.Culture))
            .WithDescription(description)
            .WithColor(Color.Orange)
            .Build();
        return new MessagePayload(null, embed, null);
    }
}
