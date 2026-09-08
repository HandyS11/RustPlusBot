using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>The collaborators <see cref="MarkerReply"/> needs to format a marker reply.</summary>
/// <param name="State">The live event state reader.</param>
/// <param name="Localizer">The reply localizer.</param>
/// <param name="Clock">Supplies "how long ago" for the suffix.</param>
/// <param name="MapSettings">Supplies the server's grid style for the reference.</param>
internal sealed record MarkerReplyServices(
    IEventState State,
    ILocalizer Localizer,
    IClock Clock,
    IMapSettingsStore MapSettings);
