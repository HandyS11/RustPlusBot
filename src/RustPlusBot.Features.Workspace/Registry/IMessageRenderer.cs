using RustPlusBot.Features.Workspace.Gateway;

namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Renders the payload for one message key. Looked up by <see cref="MessageKey"/>.</summary>
internal interface IMessageRenderer
{
    /// <summary>The message key this renderer produces (matches <see cref="MessageSpec.Key"/>).</summary>
    string MessageKey { get; }

    /// <summary>Builds the current payload for the given context.</summary>
    /// <param name="context">The render context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The message payload to post or edit.</returns>
    ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken);
}
