namespace RustPlusBot.Features.StorageMonitors.Rendering;

/// <summary>Custom ids for storage-monitor components. Tails encode "{serverId}:{entityId}".</summary>
internal static class StorageMonitorComponentIds
{
    /// <summary>Pairing-prompt Accept button; tail "{serverId}:{entityId}".</summary>
    public const string AcceptPrefix = "storage:accept:";

    /// <summary>Pairing-prompt Dismiss button; tail "{serverId}:{entityId}".</summary>
    public const string DismissPrefix = "storage:dismiss:";

    /// <summary>Refresh button (re-reads contents); tail "{serverId}:{entityId}".</summary>
    public const string RefreshPrefix = "storage:refresh:";

    /// <summary>Rename button (opens the modal); tail "{serverId}:{entityId}".</summary>
    public const string RenamePrefix = "storage:rename:";

    /// <summary>Rename modal id; tail "{serverId}:{entityId}".</summary>
    public const string RenameModalPrefix = "storage:rename:modal:";

    /// <summary>The rename modal's text input id.</summary>
    public const string RenameInputId = "storage:rename:input";
}
