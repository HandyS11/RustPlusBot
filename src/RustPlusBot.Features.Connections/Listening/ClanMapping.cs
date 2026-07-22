using System.Security.Cryptography;
using RustPlusApi.Data;
using RustPlusApi.Data.Clans;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>
/// Maps RustPlusApi clan types onto the bot's RustPlusApi-free snapshots. The ONLY place clan
/// protobuf models and <see cref="RustPlusErrorCode"/> are interpreted.
/// </summary>
internal static class ClanMapping
{
    /// <summary>
    /// Classifies a clan-info response. <c>no_clan</c> is a definitive negative; every other
    /// failure — including a success carrying no payload — is <see cref="ClanProbeStatus.Unavailable"/>,
    /// so a transient fault never looks like "the player left their clan".
    /// </summary>
    /// <param name="isSuccess">Whether the Rust+ response succeeded.</param>
    /// <param name="errorCode">The error code if the response failed, or null.</param>
    /// <param name="data">The response payload, or null.</param>
    /// <returns>The classified probe result.</returns>
    public static ClanProbeResult FromResponse(bool isSuccess, RustPlusErrorCode? errorCode, ClanInfo? data)
    {
        if (isSuccess)
        {
            return data is null ? ClanProbeResult.Unavailable : ClanProbeResult.From(ToSnapshot(data));
        }

        return errorCode == RustPlusErrorCode.NoClan ? ClanProbeResult.NoClan : ClanProbeResult.Unavailable;
    }

    /// <summary>Converts a RustPlusApi clan payload into the bot's snapshot shape.</summary>
    /// <param name="info">The clan payload to convert.</param>
    /// <returns>The equivalent <see cref="ClanSnapshot"/>.</returns>
    public static ClanSnapshot ToSnapshot(ClanInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        return new ClanSnapshot(
            info.ClanId,
            info.Name ?? string.Empty,
            Utc(info.Created),
            info.Creator,
            info.Motd,
            info.MotdTimestamp is { } motdAt ? Utc(motdAt) : null,
            info.MotdAuthor,
            HashLogo(info.Logo),
            info.Color,
            info.MaxMemberCount,
            info.Score,
            [
                .. (info.Roles ?? []).Select(r => new ClanRoleSnapshot(
                    r.RoleId, r.Rank, r.Name ?? string.Empty, r.CanSetMotd, r.CanSetLogo, r.CanInvite,
                    r.CanKick, r.CanPromote, r.CanDemote, r.CanSetPlayerNotes, r.CanAccessLogs,
                    r.CanAccessScoreEvents)),
            ],
            [
                .. (info.Members ?? []).Select(m => new ClanMemberSnapshot(
                    m.SteamId, m.RoleId, Utc(m.Joined), Utc(m.LastSeen), m.Notes, m.Online ?? false)),
            ],
            [
                .. (info.Invites ?? []).Select(i => new ClanInviteSnapshot(
                    i.SteamId, i.Recruiter, Utc(i.Timestamp))),
            ]);
    }

    /// <summary>
    /// Hashes the clan logo bytes so a change can be detected without carrying or storing the image.
    /// Not a security boundary — SHA-256 is used purely as a stable content fingerprint.
    /// </summary>
    /// <param name="logo">The raw logo bytes, or null.</param>
    /// <returns>A lowercase hex digest, or null when there is no logo.</returns>
    public static string? HashLogo(byte[]? logo)
    {
        if (logo is null || logo.Length == 0)
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(logo)).ToLowerInvariant();
    }

    /// <summary>Reinterprets an API timestamp as UTC (the API documents all clan timestamps as UTC).</summary>
    /// <param name="value">The timestamp to normalise.</param>
    /// <returns>The equivalent <see cref="DateTimeOffset"/> at zero offset.</returns>
    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
}
