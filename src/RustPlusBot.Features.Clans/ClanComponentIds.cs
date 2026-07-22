namespace RustPlusBot.Features.Clans;

/// <summary>Stable Discord component ids for the clan surfaces.</summary>
internal static class ClanComponentIds
{
    /// <summary>Prefix of the Set MOTD button id; the server id is appended.</summary>
    public const string SetMotdButtonPrefix = "clan:motd:";

    /// <summary>Prefix of the Set MOTD modal id; the server id is appended.</summary>
    public const string SetMotdModalPrefix = "clan:motdmodal:";

    /// <summary>Id of the MOTD text input inside the modal.</summary>
    public const string MotdInputId = "clan:motd:text";
}
