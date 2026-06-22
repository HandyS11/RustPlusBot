namespace RustPlusBot.Features.Alarms.Rendering;

/// <summary>Custom ids for alarm components. Tails encode "{serverId}:{entityId}".</summary>
internal static class AlarmComponentIds
{
    /// <summary>Pairing-prompt Accept button; tail "{serverId}:{entityId}".</summary>
    public const string AcceptPrefix = "alarm:accept:";

    /// <summary>Pairing-prompt Dismiss button; tail "{serverId}:{entityId}".</summary>
    public const string DismissPrefix = "alarm:dismiss:";

    /// <summary>Ping @everyone toggle button; tail "{serverId}:{entityId}".</summary>
    public const string PingTogglePrefix = "alarm:ping:";

    /// <summary>Relay to team chat toggle button; tail "{serverId}:{entityId}".</summary>
    public const string RelayTogglePrefix = "alarm:relay:";

    /// <summary>Rename button (opens the modal); tail "{serverId}:{entityId}".</summary>
    public const string RenamePrefix = "alarm:rename:";

    /// <summary>Rename modal id; tail "{serverId}:{entityId}".</summary>
    public const string RenameModalPrefix = "alarm:rename:modal:";

    /// <summary>The rename modal's text input id.</summary>
    public const string RenameInputId = "alarm:rename:input";
}
