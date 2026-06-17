using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Filters team members by an optional name argument for !prox and !steamid.</summary>
internal static class TeamMemberFilter
{
    /// <summary>Returns all members when <paramref name="nameArg"/> is null/whitespace,
    /// otherwise those whose name contains it (case-insensitive).</summary>
    /// <param name="members">The members to filter.</param>
    /// <param name="nameArg">The optional partial name filter.</param>
    /// <returns>The matching members (all when no filter is given).</returns>
    public static IReadOnlyList<TeamMemberSnapshot> ByName(
        IReadOnlyList<TeamMemberSnapshot> members,
        string? nameArg)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (string.IsNullOrWhiteSpace(nameArg))
        {
            return members;
        }

        return members
            .Where(m => m.Name.Contains(nameArg, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
