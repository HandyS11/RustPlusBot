namespace RustPlusBot.Features.Switches.Rendering;

/// <summary>Custom ids for switch components. Tails encode "{serverId}:{entityId}".</summary>
internal static class SwitchComponentIds
{
    /// <summary>Pairing-prompt Accept button; tail "{serverId}:{entityId}".</summary>
    public const string AcceptPrefix = "switch:accept:";

    /// <summary>Pairing-prompt Dismiss button; tail "{serverId}:{entityId}".</summary>
    public const string DismissPrefix = "switch:dismiss:";

    /// <summary>Turn-on button; tail "{serverId}:{entityId}".</summary>
    public const string OnPrefix = "switch:on:";

    /// <summary>Turn-off button; tail "{serverId}:{entityId}".</summary>
    public const string OffPrefix = "switch:off:";

    /// <summary>Strobe button; tail "{serverId}:{entityId}".</summary>
    public const string StrobePrefix = "switch:strobe:";

    /// <summary>Rename button (opens the modal); tail "{serverId}:{entityId}".</summary>
    public const string RenamePrefix = "switch:rename:";

    /// <summary>Rename modal id; tail "{serverId}:{entityId}".</summary>
    public const string RenameModalPrefix = "switch:rename:modal:";

    /// <summary>The rename modal's text input id.</summary>
    public const string RenameInputId = "switch:rename:input";
}
