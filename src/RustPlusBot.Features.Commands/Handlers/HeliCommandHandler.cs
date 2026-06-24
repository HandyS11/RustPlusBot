using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!heli — reports the patrol helicopter's current grid position, if any.</summary>
/// <param name="state">The live event state.</param>
/// <param name="localizer">The reply localizer.</param>
/// <param name="clock">For "how long ago".</param>
internal sealed class HeliCommandHandler(IEventState state, ILocalizer localizer, IClock clock)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "heli";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult<string?>(
            MarkerReply.For(state, context, MarkerKind.PatrolHelicopter, "command.heli", localizer, clock));
    }
}
