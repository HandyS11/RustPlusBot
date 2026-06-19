using Discord;
using Discord.Interactions;
using RustPlusBot.Features.Switches.Rendering;

namespace RustPlusBot.Features.Switches.Modules;

/// <summary>The modal that collects a new switch name. Handled by <see cref="SwitchComponentModule"/>.</summary>
public sealed class SwitchRenameModal : IModal
{
    /// <summary>The new name.</summary>
    [InputLabel("Switch name")]
    [ModalTextInput(SwitchComponentIds.RenameInputId, TextInputStyle.Short, maxLength: 128)]
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Title => "Rename switch";
}
