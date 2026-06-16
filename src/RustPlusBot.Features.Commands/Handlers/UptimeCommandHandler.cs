using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!uptime — reports how long the bot process has been running.</summary>
/// <param name="uptime">The process-uptime baseline.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class UptimeCommandHandler(BotUptime uptime, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "uptime";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult<string?>(
            localizer.Get("command.uptime.ok", context.Culture, DurationFormat.Compact(uptime.Elapsed)));
    }
}
