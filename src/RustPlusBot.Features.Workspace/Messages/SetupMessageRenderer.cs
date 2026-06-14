using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders the global #setup instructions (the interactive button arrives in 1b).</summary>
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
        return ValueTask.FromResult(new MessagePayload(null, embed, null));
    }
}
