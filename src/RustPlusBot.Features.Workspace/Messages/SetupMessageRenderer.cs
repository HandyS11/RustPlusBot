using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders the global #setup instructions with a Connect account button.</summary>
/// <param name="localizer">String resolution.</param>
internal sealed class SetupMessageRenderer(ILocalizer localizer) : IMessageRenderer
{
    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.SetupMain;

    /// <inheritdoc />
    public ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("setup.title", context.Culture))
            .WithDescription(localizer.Get("setup.body", context.Culture))
            .WithColor(Color.Blue)
            .Build();
        var components = new ComponentBuilder()
            .WithButton(
                localizer.Get("setup.connect.button", context.Culture),
                WorkspaceComponentIds.ConnectAccount,
                ButtonStyle.Primary)
            .WithButton(
                localizer.Get("setup.disconnect.button", context.Culture),
                WorkspaceComponentIds.DisconnectAccount,
                ButtonStyle.Danger)
            .Build();
        return ValueTask.FromResult(new MessagePayload(null, embed, components));
    }
}
