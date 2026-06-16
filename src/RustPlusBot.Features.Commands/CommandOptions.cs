namespace RustPlusBot.Features.Commands;

/// <summary>Configuration for the in-game command framework.</summary>
public sealed class CommandOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Commands";

    /// <summary>Fallback command prefix when a server has no configured prefix.</summary>
    public string DefaultPrefix { get; set; } = "!";

    /// <summary>Minimum interval between identical commands on one server.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(4);
}
