using Discord;
using Discord.Interactions;
using RustPlusBot.Features.Alarms.Rendering;

namespace RustPlusBot.Features.Alarms.Modules;

/// <summary>The modal that collects a new alarm name. Handled by <see cref="AlarmComponentModule"/>.</summary>
public sealed class AlarmRenameModal : IModal
{
    /// <summary>The new name.</summary>
    [InputLabel("Alarm name")]
    [ModalTextInput(AlarmComponentIds.RenameInputId, TextInputStyle.Short, maxLength: 128)]
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Title => "Rename alarm";
}
