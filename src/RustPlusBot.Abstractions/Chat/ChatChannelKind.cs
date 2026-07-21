namespace RustPlusBot.Abstractions.Chat;

/// <summary>Which in-game chat channel a relayed line belongs to.</summary>
public enum ChatChannelKind
{
    /// <summary>In-game team chat.</summary>
    Team = 0,

    /// <summary>In-game clan chat.</summary>
    Clan = 1,
}
