using Discord;
using Discord.Interactions;
using RustPlusBot.Features.StorageMonitors.Rendering;

namespace RustPlusBot.Features.StorageMonitors.Modules;

/// <summary>The modal that collects a new storage-monitor name. Handled by the storage-monitor component module.</summary>
public sealed class StorageMonitorRenameModal : IModal
{
    /// <summary>The new name.</summary>
    [InputLabel("Storage monitor name")]
    [ModalTextInput(StorageMonitorComponentIds.RenameInputId, TextInputStyle.Short, maxLength: 128)]
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Title => "Rename storage monitor";
}
