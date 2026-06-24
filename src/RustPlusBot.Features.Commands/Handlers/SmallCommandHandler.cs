using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!small — reports the small oil rig's status.</summary>
/// <param name="rigState">The rig state reader.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class SmallCommandHandler(IRigState rigState, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "small";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult<string?>(RigReply.For(rigState, context, RigKind.Small, "command.small", localizer));
    }
}
