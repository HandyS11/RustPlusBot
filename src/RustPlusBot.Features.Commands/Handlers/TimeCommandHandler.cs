using System.Globalization;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!time — reports the in-game clock and whether it is day or night.</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class TimeCommandHandler(IRustServerQuery query, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "time";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var time = await query.GetTimeAsync(context.GuildId, context.ServerId, cancellationToken).ConfigureAwait(false);
        if (time is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        var isDay = time.TimeOfDay >= time.Sunrise && time.TimeOfDay < time.Sunset;
        var phase = localizer.Get(isDay ? "command.time.day" : "command.time.night", context.Culture);

        var hours = (int)time.TimeOfDay;
        var minutes = (int)((time.TimeOfDay - hours) * 60f);
        var clockText = string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}");

        return localizer.Get("command.time.ok", context.Culture, clockText, phase);
    }
}
