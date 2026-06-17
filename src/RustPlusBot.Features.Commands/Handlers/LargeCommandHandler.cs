using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!large — reports the large oil rig's status.</summary>
/// <param name="rigState">The rig state reader.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class LargeCommandHandler(IRigState rigState, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "large";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult<string?>(RigReply.For(rigState, context, RigKind.Large, "command.large", localizer));
    }
}
