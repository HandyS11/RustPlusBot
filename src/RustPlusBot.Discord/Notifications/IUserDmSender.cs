namespace RustPlusBot.Discord.Notifications;

/// <summary>Sends a direct message to a Discord user. A closed DM (or any send failure) is swallowed and logged.</summary>
public interface IUserDmSender
{
    /// <summary>Best-effort DM to <paramref name="userId"/>; never throws.</summary>
    /// <param name="userId">The Discord user snowflake.</param>
    /// <param name="message">The message text.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the send was attempted.</returns>
    Task SendAsync(ulong userId, string message, CancellationToken cancellationToken = default);
}
