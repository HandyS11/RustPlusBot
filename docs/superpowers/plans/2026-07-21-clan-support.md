# Clan Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect whether a paired player is in a Rust clan and, only while one exists, provision a bidirectional `#clanchat` bridge and a `#claninfo` channel carrying pinned self-refreshing embeds plus a live clan-change feed.

**Architecture:** A new `RustPlusBot.Features.Clans` module consumes clan events published by the existing `ConnectionSupervisor` over `IEventBus`, persists the latest clan snapshot per `(GuildId, ServerId)`, and diffs consecutive snapshots to emit feed events. Channel conditionality is a new generic capability seam on the Workspace reconciler, not clan-specific branching. The chat bridge is a structural clone of the proven `Features.Chat` team-chat bridge.

**Tech Stack:** .NET 10, Discord.Net, RustPlusApi 2.0.0-beta.4, EF Core 10 + SQLite, xUnit + NSubstitute, Serilog.

**Spec:** `docs/superpowers/specs/2026-07-21-clan-support-design.md`

## Global Constraints

Every task's requirements implicitly include this section.

- Solution is `RustPlusBot.slnx`. There is no `.sln`.
- **`-maxcpucount:1` is MANDATORY on every `dotnet build` and every `dotnet test`.** A `ConfigureGitHooks` target races on `.git/config` under parallel builds; a failed build silently drops an assembly's tests so they report 0 and look like they passed. Always read per-assembly test counts, never just "passed".
- Run `dotnet tool restore` once before the first build.
- Build is `-warnaserror` with `AnalysisLevel=latest-all`. XML `///` docs are required on **every** public **and internal** type *and member*, including record positional parameters (`/// <param name="X">`).
- `CA1305`/`CA1307`/`CA1310`: pass `CultureInfo.InvariantCulture` on every format/parse and `StringComparison.Ordinal` on every string comparison.
- `CA2007`: `.ConfigureAwait(false)` on every awaited task in `src/`. Test projects are exempt.
- Broad `catch (Exception)` is allowed ONLY with an inline `#pragma warning disable CA1031` carrying a one-line justification comment, paired with a `[LoggerMessage]`-generated `static partial` log method.
- **A broad catch in a method that takes a CALLER-supplied `CancellationToken` AND returns a fallback value on failure MUST be guarded `catch (Exception ex) when (!cancellationToken.IsCancellationRequested)`**, so caller-initiated cancellation propagates instead of being logged as a fault and swallowed into that fallback. Every timeout-guarded method in `RustPlusSocketSource` does this — match it.
  **This rule does NOT apply to hosted-service consumer loops**, which own their own `CancellationTokenSource`, already catch `OperationCanceledException` in a preceding clause, and are joined by a `StopAsync` that only swallows OCE. Adding the guard there would let a non-OCE exception thrown after cancellation escape and fault host shutdown. Leave those catches unguarded.
- Tests: plain xUnit `Assert.*` + NSubstitute. **No FluentAssertions.** `using Xunit` is a global using — never add it per file. Tests do not need `.ConfigureAwait(false)`.
- Every new string key MUST be added to **both** `src/RustPlusBot.Localization/Strings.resx` and `Strings.fr.resx`. `StringsResourceParityTests` fails the build otherwise.
- `dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder"` is a hard CI gate that fails on any diff. It is slow — run it ONCE at the very end (Task 13), not per task.
- Commit messages: plain imperative sentences, **no `feat:`/`fix:` prefixes**, each ending with a `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` trailer.
- Branch: `feat/clan-support`, off `develop`.
- Singletons must NEVER capture a scoped `I*Store`. Resolve per call from `IServiceScopeFactory.CreateAsyncScope()`.
- Every feature module exposes exactly one public `Add<Feature>()` extension at project root; everything else is `internal sealed`.

## Verified API facts

These were confirmed by reflecting over `RustPlusApi 2.0.0-beta.4`. Do not re-derive or assume otherwise.

```
ClanInfo            : long ClanId, string Name, DateTime Created, ulong Creator,
                      string? Motd, DateTime? MotdTimestamp, ulong? MotdAuthor,
                      byte[]? Logo, int? Color, IEnumerable<ClanRole> Roles,
                      IEnumerable<ClanMember> Members, IEnumerable<ClanInvite> Invites,
                      int? MaxMemberCount, long? Score
ClanMember          : ulong SteamId, int RoleId, DateTime Joined, DateTime LastSeen,
                      string? Notes, bool? Online            <-- NULLABLE
ClanRole            : int RoleId, int Rank, string Name, bool CanSetMotd, CanSetLogo,
                      CanInvite, CanKick, CanPromote, CanDemote, CanSetPlayerNotes,
                      CanAccessLogs, CanAccessScoreEvents
ClanInvite          : ulong SteamId, ulong Recruiter, DateTime Timestamp
ClanMessageEventArg : long ClanId, ulong SteamId, string Name, string Message, DateTime Time   (FLAT)
ClanChangedEventArg : ClanInfo? ClanInfo                     (null => dissolved/left)

IRustPlus.GetClanInfoAsync(ct)          -> Task<Response<ClanInfo>>
IRustPlus.SendClanMessageAsync(msg, ct) -> Task<Response>
IRustPlus.SetClanMotdAsync(motd, ct)    -> Task<Response>
IRustPlus.OnClanChatReceived            -> EventHandler<ClanMessageEventArg>
IRustPlus.OnClanChanged                 -> EventHandler<ClanChangedEventArg>
RustPlusErrorCode.NoClan                -> "no_clan"

Response<T> : bool IsSuccess, T? Data, ErrorMessage? Error   (Error.Code is RustPlusErrorCode)
```

Lower `ClanRole.Rank` = higher rank (leader is the lowest number).

## File Structure

**New project `src/RustPlusBot.Features.Clans/`**

| File | Responsibility |
| --- | --- |
| `ClansServiceCollectionExtensions.cs` | The single public `AddClans()` |
| `Hosting/ClansHostedService.cs` | One event-bus consumer loop applying clan state changes |
| `State/ClanStateService.cs` | Applies a `ClanStateChangedEvent`: persist, diff, trigger reconcile |
| `State/ClanSnapshotDiffer.cs` | Pure `(previous, current) -> IReadOnlyList<ClanChange>` |
| `State/ClanChange.cs` | The typed change record + `ClanChangeKind` enum |
| `State/ClanCapabilityProvider.cs` | `IWorkspaceCapabilityProvider` for `"clan"` |
| `Posting/IClanFeedPoster.cs` / `DiscordClanFeedPoster.cs` | Posts feed lines into `#claninfo` |

**Note:** there is no clan chat bridge in this project. `Features.Chat` is generalised over `ChatChannelKind` in Tasks 6-7 and serves both team and clan chat from one implementation.
| `Names/IClanNameResolver.cs` / `ClanNameResolver.cs` | Steam id → display name, with profile-link fallback |
| `Messages/ClanOverviewMessageRenderer.cs` | `clan.overview` embed + Set MOTD button |
| `Messages/ClanRosterMessageRenderer.cs` | `clan.roster` embed |
| `Messages/ClanInvitesMessageRenderer.cs` | `clan.invites` embed (empty when none) |
| `Messages/ClanChangeRenderer.cs` | Renders one `ClanChange` to feed text |
| `Modules/ClanMotdModule.cs` | Button + modal interaction handler |
| `ClanComponentIds.cs` | Component id constants |

**Modified — Abstractions**

| File | Change |
| --- | --- |
| `Connections/ClanSnapshot.cs` (new) | `ClanSnapshot`, `ClanMemberSnapshot`, `ClanRoleSnapshot`, `ClanInviteSnapshot` |
| `Connections/ClanProbeResult.cs` (new) | `ClanProbeResult` + `ClanProbeStatus` |
| `Events/ClanMessageReceivedEvent.cs` (new) | Game→Discord relay event |
| `Events/ClanStateChangedEvent.cs` (new) | Snapshot-changed event |

**Modified — Connections, Domain, Persistence, Workspace, Localization, Host** — enumerated per task.

**New test project `tests/RustPlusBot.Features.Clans.Tests/`** mirroring the source layout.

---

### Task 1: Clan snapshot records and events (Abstractions)

Pure data types with no dependencies. Nothing to unit test in isolation — these are records — so this task's gate is that the solution still builds with docs and analyzers clean.

**Files:**
- Create: `src/RustPlusBot.Abstractions/Connections/ClanSnapshot.cs`
- Create: `src/RustPlusBot.Abstractions/Connections/ClanProbeResult.cs`
- Create: `src/RustPlusBot.Abstractions/Events/ClanMessageReceivedEvent.cs`
- Create: `src/RustPlusBot.Abstractions/Events/ClanStateChangedEvent.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ClanSnapshot`, `ClanMemberSnapshot`, `ClanRoleSnapshot`, `ClanInviteSnapshot`, `ClanProbeResult`, `ClanProbeStatus`, `ClanMessageReceivedEvent`, `ClanStateChangedEvent`. Every later task depends on these exact shapes.

- [ ] **Step 1: Create the snapshot records**

`src/RustPlusBot.Abstractions/Connections/ClanSnapshot.cs`:

```csharp
namespace RustPlusBot.Abstractions.Connections;

/// <summary>A permission role within a clan.</summary>
/// <param name="RoleId">Unique id of the role within the clan.</param>
/// <param name="Rank">Rank order; LOWER values are HIGHER ranks (0 is the leader).</param>
/// <param name="Name">Display name of the role.</param>
/// <param name="CanSetMotd">True when holders may set the clan MOTD.</param>
/// <param name="CanSetLogo">True when holders may set the clan logo.</param>
/// <param name="CanInvite">True when holders may invite players.</param>
/// <param name="CanKick">True when holders may kick members.</param>
/// <param name="CanPromote">True when holders may promote members.</param>
/// <param name="CanDemote">True when holders may demote members.</param>
/// <param name="CanSetPlayerNotes">True when holders may set notes on members.</param>
/// <param name="CanAccessLogs">True when holders may view clan audit logs.</param>
/// <param name="CanAccessScoreEvents">True when holders may view clan score events.</param>
public sealed record ClanRoleSnapshot(
    int RoleId,
    int Rank,
    string Name,
    bool CanSetMotd,
    bool CanSetLogo,
    bool CanInvite,
    bool CanKick,
    bool CanPromote,
    bool CanDemote,
    bool CanSetPlayerNotes,
    bool CanAccessLogs,
    bool CanAccessScoreEvents);

/// <summary>A member of a clan.</summary>
/// <param name="SteamId">Steam64 id of the member.</param>
/// <param name="RoleId">Id of the role assigned to this member.</param>
/// <param name="Joined">When the member joined the clan (UTC).</param>
/// <param name="LastSeen">When the member was last seen online (UTC).</param>
/// <param name="Notes">Officer notes attached to this member, or null.</param>
/// <param name="Online">True when the member is currently online. The API reports this as nullable; null is mapped to false.</param>
public sealed record ClanMemberSnapshot(
    ulong SteamId,
    int RoleId,
    DateTimeOffset Joined,
    DateTimeOffset LastSeen,
    string? Notes,
    bool Online);

/// <summary>A pending invitation to join a clan.</summary>
/// <param name="SteamId">Steam64 id of the invited player.</param>
/// <param name="Recruiter">Steam64 id of the member who sent the invitation.</param>
/// <param name="Timestamp">When the invitation was created (UTC).</param>
public sealed record ClanInviteSnapshot(ulong SteamId, ulong Recruiter, DateTimeOffset Timestamp);

/// <summary>A full clan snapshot, decoupled from RustPlusApi types.</summary>
/// <param name="ClanId">Unique identifier of the clan.</param>
/// <param name="Name">Display name of the clan.</param>
/// <param name="Created">When the clan was created (UTC).</param>
/// <param name="Creator">Steam64 id of the clan creator.</param>
/// <param name="Motd">Message of the day, or null when unset.</param>
/// <param name="MotdTimestamp">When the MOTD was last changed (UTC), or null.</param>
/// <param name="MotdAuthor">Steam64 id of the player who last changed the MOTD, or null.</param>
/// <param name="LogoHash">Stable hash of the clan logo bytes, or null when no logo is set. The bytes themselves are not carried: nothing renders them.</param>
/// <param name="Color">Clan colour as a packed ARGB integer, or null.</param>
/// <param name="MaxMemberCount">Maximum members allowed, or null when uncapped.</param>
/// <param name="Score">Clan score, or null when the server does not report one.</param>
/// <param name="Roles">Roles defined in this clan.</param>
/// <param name="Members">Current members of the clan.</param>
/// <param name="Invites">Pending invitations to the clan.</param>
public sealed record ClanSnapshot(
    long ClanId,
    string Name,
    DateTimeOffset Created,
    ulong Creator,
    string? Motd,
    DateTimeOffset? MotdTimestamp,
    ulong? MotdAuthor,
    string? LogoHash,
    int? Color,
    int? MaxMemberCount,
    long? Score,
    IReadOnlyList<ClanRoleSnapshot> Roles,
    IReadOnlyList<ClanMemberSnapshot> Members,
    IReadOnlyList<ClanInviteSnapshot> Invites);
```

- [ ] **Step 2: Create the probe result**

`src/RustPlusBot.Abstractions/Connections/ClanProbeResult.cs`:

```csharp
namespace RustPlusBot.Abstractions.Connections;

/// <summary>The outcome of asking a live socket for its clan snapshot.</summary>
public enum ClanProbeStatus
{
    /// <summary>The player is in a clan and the snapshot is populated.</summary>
    HasClan = 0,

    /// <summary>The server answered definitively that the player is in no clan.</summary>
    NoClan = 1,

    /// <summary>The request failed or timed out; the previous known state must be preserved.</summary>
    Unavailable = 2,
}

/// <summary>
/// A clan probe outcome. <see cref="ClanProbeStatus.NoClan"/> and
/// <see cref="ClanProbeStatus.Unavailable"/> are deliberately distinct: collapsing them into a
/// single null snapshot would let a transient socket failure tear down a guild's clan channels.
/// </summary>
/// <param name="Status">What the server said.</param>
/// <param name="Snapshot">The snapshot; non-null if and only if <paramref name="Status"/> is <see cref="ClanProbeStatus.HasClan"/>.</param>
public sealed record ClanProbeResult(ClanProbeStatus Status, ClanSnapshot? Snapshot)
{
    /// <summary>A probe that definitively reported no clan.</summary>
    public static ClanProbeResult NoClan { get; } = new(ClanProbeStatus.NoClan, null);

    /// <summary>A probe that failed; the caller must preserve the last known state.</summary>
    public static ClanProbeResult Unavailable { get; } = new(ClanProbeStatus.Unavailable, null);

    /// <summary>Creates a successful probe carrying <paramref name="snapshot"/>.</summary>
    /// <param name="snapshot">The clan snapshot returned by the server.</param>
    /// <returns>A <see cref="ClanProbeStatus.HasClan"/> result.</returns>
    public static ClanProbeResult From(ClanSnapshot snapshot) => new(ClanProbeStatus.HasClan, snapshot);
}
```

- [ ] **Step 3: Create the two bus events**

`src/RustPlusBot.Abstractions/Events/ClanMessageReceivedEvent.cs`:

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when an in-game clan chat line is received, so the bridge can relay it to Discord.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server whose socket received the line.</param>
/// <param name="SenderSteamId">The Steam64 id of the in-game sender.</param>
/// <param name="SenderName">The in-game display name of the sender.</param>
/// <param name="Message">The message text.</param>
/// <param name="FromActivePlayer">True when the sender is the bot's active player (used to drop relay echoes).</param>
public sealed record ClanMessageReceivedEvent(
    ulong GuildId,
    Guid ServerId,
    ulong SenderSteamId,
    string SenderName,
    string Message,
    bool FromActivePlayer);
```

`src/RustPlusBot.Abstractions/Events/ClanStateChangedEvent.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// Published when a server's clan snapshot may have changed — on connect (probe) and on every
/// in-game clan change. Carries the probe status so consumers can tell "definitely no clan"
/// apart from "could not ask".
/// </summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server the snapshot belongs to.</param>
/// <param name="Status">Whether the player has a clan, has none, or could not be asked.</param>
/// <param name="Snapshot">The new snapshot; non-null if and only if <paramref name="Status"/> is <see cref="ClanProbeStatus.HasClan"/>.</param>
public sealed record ClanStateChangedEvent(
    ulong GuildId,
    Guid ServerId,
    ClanProbeStatus Status,
    ClanSnapshot? Snapshot);
```

- [ ] **Step 4: Build and verify clean**

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. Any missing-XML-doc warning is an error here — fix it before continuing.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Abstractions/Connections/ClanSnapshot.cs \
        src/RustPlusBot.Abstractions/Connections/ClanProbeResult.cs \
        src/RustPlusBot.Abstractions/Events/ClanMessageReceivedEvent.cs \
        src/RustPlusBot.Abstractions/Events/ClanStateChangedEvent.cs
git commit -m "$(cat <<'EOF'
Add clan snapshot records and bus events

Introduce the RustPlusApi-free clan snapshot shapes, the three-state clan
probe result, and the two event-bus events the clan feature consumes.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Clan mapping and the connection seam (Connections)

`RustPlusSocketSource` is an untested integration shim, so all interpretable logic goes into a testable static `ClanMapping` — exactly how `ReachabilityMapping` / `ReachabilityMappingTests` already work in this project.

**Files:**
- Create: `src/RustPlusBot.Features.Connections/Listening/ClanMapping.cs`
- Create: `src/RustPlusBot.Features.Connections/Listening/ClanChatLine.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ClanMappingTests.cs`
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`

**Interfaces:**
- Consumes: `ClanSnapshot`, `ClanMemberSnapshot`, `ClanRoleSnapshot`, `ClanInviteSnapshot`, `ClanProbeResult`, `ClanProbeStatus` (Task 1).
- Produces:
  - `internal static class ClanMapping` with `public static ClanProbeResult FromResponse(bool isSuccess, RustPlusErrorCode? errorCode, ClanInfo? data)` and `public static ClanSnapshot ToSnapshot(ClanInfo info)` and `public static string HashLogo(byte[]? logo)`.
  - `internal sealed record ClanChatLine(ulong SteamId, string Name, string Message, DateTimeOffset Time)`.

**Not produced here:** there is no `IClanChatSender`. Task 6 replaces `ITeamChatSender` with a single kind-parameterised `IChatSender` that serves both channels, so a second same-shaped sender interface is never created.
  - New `IRustServerConnection` members: `Task<ClanProbeResult> GetClanInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)`, `Task SendClanMessageAsync(string message, CancellationToken cancellationToken)`, `Task<bool> SetClanMotdAsync(string motd, TimeSpan timeout, CancellationToken cancellationToken)`, `event EventHandler<ClanChatLine>? ClanMessageReceived`, `event EventHandler<ClanProbeResult>? ClanChanged`.

**Note on `ClanChanged`:** the event carries `ClanProbeResult`, not `ClanSnapshot?`. `OnClanChanged` with a null `ClanInfo` maps to `ClanProbeResult.NoClan` — a definitive signal — which keeps a single type flowing from socket to supervisor to bus.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Connections.Tests/ClanMappingTests.cs`:

```csharp
using RustPlusApi.Data;
using RustPlusApi.Data.Clans;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ClanMappingTests
{
    [Fact]
    public void Maps_no_clan_error_to_NoClan()
    {
        var result = ClanMapping.FromResponse(false, RustPlusErrorCode.NoClan, null);

        Assert.Equal(ClanProbeStatus.NoClan, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Maps_any_other_error_to_Unavailable()
    {
        var result = ClanMapping.FromResponse(false, RustPlusErrorCode.NotFound, null);

        Assert.Equal(ClanProbeStatus.Unavailable, result.Status);
    }

    [Fact]
    public void Maps_a_missing_error_code_to_Unavailable()
    {
        var result = ClanMapping.FromResponse(false, null, null);

        Assert.Equal(ClanProbeStatus.Unavailable, result.Status);
    }

    [Fact]
    public void Maps_success_with_null_payload_to_Unavailable()
    {
        // A success carrying no data is not evidence of "no clan"; preserve the last known state.
        var result = ClanMapping.FromResponse(true, null, null);

        Assert.Equal(ClanProbeStatus.Unavailable, result.Status);
    }

    [Fact]
    public void Maps_a_populated_clan_to_a_snapshot()
    {
        var info = new ClanInfo
        {
            ClanId = 42,
            Name = "Wolves",
            Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            Creator = 7UL,
            Motd = "hold the line",
            MotdTimestamp = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            MotdAuthor = 9UL,
            Logo = [1, 2, 3],
            Color = 255,
            MaxMemberCount = 8,
            Score = 1234,
            Roles = [new ClanRole { RoleId = 1, Rank = 0, Name = "Leader", CanSetMotd = true }],
            Members =
            [
                new ClanMember
                {
                    SteamId = 7UL,
                    RoleId = 1,
                    Joined = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                    LastSeen = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    Notes = "founder",
                    Online = true,
                },
            ],
            Invites = [new ClanInvite { SteamId = 11UL, Recruiter = 7UL, Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc) }],
        };

        var result = ClanMapping.FromResponse(true, null, info);

        Assert.Equal(ClanProbeStatus.HasClan, result.Status);
        var snapshot = Assert.IsType<ClanSnapshot>(result.Snapshot);
        Assert.Equal(42L, snapshot.ClanId);
        Assert.Equal("Wolves", snapshot.Name);
        Assert.Equal(7UL, snapshot.Creator);
        Assert.Equal(1234L, snapshot.Score);
        Assert.Equal(8, snapshot.MaxMemberCount);
        Assert.Single(snapshot.Roles);
        Assert.True(snapshot.Roles[0].CanSetMotd);
        Assert.Single(snapshot.Members);
        Assert.True(snapshot.Members[0].Online);
        Assert.Equal("founder", snapshot.Members[0].Notes);
        Assert.Single(snapshot.Invites);
        Assert.Equal(11UL, snapshot.Invites[0].SteamId);
        Assert.NotNull(snapshot.LogoHash);
    }

    [Fact]
    public void Treats_a_null_Online_flag_as_offline()
    {
        // ClanMember.Online is bool? in the API; null must not be rendered as online.
        var info = NewClan(members: [new ClanMember { SteamId = 7UL, RoleId = 1, Online = null }]);

        var result = ClanMapping.FromResponse(true, null, info);

        Assert.False(result.Snapshot!.Members[0].Online);
    }

    [Fact]
    public void Treats_timestamps_as_utc()
    {
        var info = NewClan();
        info.Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

        var result = ClanMapping.FromResponse(true, null, info);

        Assert.Equal(TimeSpan.Zero, result.Snapshot!.Created.Offset);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), result.Snapshot.Created.UtcDateTime);
    }

    [Fact]
    public void Hashes_a_missing_logo_to_null_and_equal_logos_to_equal_hashes()
    {
        Assert.Null(ClanMapping.HashLogo(null));
        Assert.Null(ClanMapping.HashLogo([]));
        Assert.Equal(ClanMapping.HashLogo([1, 2, 3]), ClanMapping.HashLogo([1, 2, 3]));
        Assert.NotEqual(ClanMapping.HashLogo([1, 2, 3]), ClanMapping.HashLogo([3, 2, 1]));
    }

    private static ClanInfo NewClan(IEnumerable<ClanMember>? members = null) =>
        new()
        {
            ClanId = 1,
            Name = "C",
            Creator = 1UL,
            Roles = [],
            Members = members ?? [],
            Invites = [],
        };
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1 --filter FullyQualifiedName~ClanMappingTests`
Expected: FAIL — compile error, `ClanMapping` does not exist.

- [ ] **Step 3: Implement `ClanMapping`**

`src/RustPlusBot.Features.Connections/Listening/ClanMapping.cs`:

```csharp
using System.Globalization;
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
```

Note: `CultureInfo` is imported for consistency with sibling files; if the analyzer reports it unused, delete the `using`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1 --filter FullyQualifiedName~ClanMappingTests`
Expected: PASS, 8 tests.

- [ ] **Step 5: Add the chat line record**

`src/RustPlusBot.Features.Connections/Listening/ClanChatLine.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>A raw clan chat line as received from a socket (before active-player classification).</summary>
/// <param name="SteamId">The Steam64 id of the sender.</param>
/// <param name="Name">The in-game display name of the sender.</param>
/// <param name="Message">The message text.</param>
/// <param name="Time">When the line was sent (UTC).</param>
internal sealed record ClanChatLine(ulong SteamId, string Name, string Message, DateTimeOffset Time);
```

- [ ] **Step 6: Extend `IRustServerConnection`**

Append these members to `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`, immediately after `SendTeamMessageAsync`:

```csharp
    /// <summary>Probes the authenticated player's clan, distinguishing "no clan" from "could not ask".</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The classified probe result.</returns>
    Task<ClanProbeResult> GetClanInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Sends a message to in-game clan chat.</summary>
    /// <param name="message">The message text to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the send has been issued.</returns>
    /// <remarks>Like <see cref="SendTeamMessageAsync"/>, this surfaces failures to the caller.</remarks>
    Task SendClanMessageAsync(string message, CancellationToken cancellationToken);

    /// <summary>Sets the clan message of the day; returns true on success.</summary>
    /// <param name="motd">The new message of the day.</param>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the MOTD was set; false on failure/timeout.</returns>
    Task<bool> SetClanMotdAsync(string motd, TimeSpan timeout, CancellationToken cancellationToken);
```

and these events after `TeamMessageReceived`:

```csharp
    /// <summary>Raised for every in-game clan chat line received on this socket.</summary>
    event EventHandler<ClanChatLine>? ClanMessageReceived;

    /// <summary>
    /// Raised when the clan snapshot changes in game. A dissolved or departed clan arrives as
    /// <see cref="ClanProbeStatus.NoClan"/> — a definitive signal, never <see cref="ClanProbeStatus.Unavailable"/>.
    /// </summary>
    event EventHandler<ClanProbeResult>? ClanChanged;
```

- [ ] **Step 7: Implement both `IRustServerConnection` implementations**

In `RejectedConnection` (`RustPlusSocketSource.cs`), add after `SendTeamMessageAsync`:

```csharp
        public Task<ClanProbeResult> GetClanInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(ClanProbeResult.Unavailable);

        public Task SendClanMessageAsync(string message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> SetClanMotdAsync(string motd, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(false);
```

and after the `TeamMessageReceived` event stub:

```csharp
        public event EventHandler<ClanChatLine>? ClanMessageReceived
        {
            add { _ = value; }
            remove { _ = value; }
        }

        public event EventHandler<ClanProbeResult>? ClanChanged
        {
            add { _ = value; }
            remove { _ = value; }
        }
```

A rejected credential returns `Unavailable`, never `NoClan`: it never asked, so it must not cause a teardown.

In `RustPlusServerConnection`, subscribe in the constructor alongside the existing handlers:

```csharp
            _rustPlus.OnClanChatReceived += OnClanChatReceived;
            _rustPlus.OnClanChanged += OnClanChanged;
```

and add the members (place them next to `SendTeamMessageAsync`):

```csharp
        public event EventHandler<ClanChatLine>? ClanMessageReceived;

        public event EventHandler<ClanProbeResult>? ClanChanged;

        public async Task<ClanProbeResult> GetClanInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.4): GetClanInfoAsync returns Task<Response<ClanInfo>>;
                // Response.IsSuccess / .Data / .Error?.Code are the accessors.
                var response = await _rustPlus.GetClanInfoAsync(timeoutCts.Token).ConfigureAwait(false);
                return ClanMapping.FromResponse(response.IsSuccess, response.Error?.Code, response.Data);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ClanProbeResult.Unavailable;
            }
#pragma warning disable CA1031 // Broad catch: a failed clan probe must degrade to Unavailable, never crash the caller.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return ClanProbeResult.Unavailable;
            }
        }

        public async Task SendClanMessageAsync(string message, CancellationToken cancellationToken)
        {
            // Intentional: send failures propagate to the caller (the supervisor classifies them).
            await _rustPlus.SendClanMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> SetClanMotdAsync(string motd, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                var response = await _rustPlus.SetClanMotdAsync(motd, timeoutCts.Token).ConfigureAwait(false);
                return response.IsSuccess;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
#pragma warning disable CA1031 // Broad catch: a failed MOTD write is reported to the user, not thrown.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return false;
            }
        }

        private void OnClanChatReceived(object? sender, ClanMessageEventArg e) =>
            ClanMessageReceived?.Invoke(this,
                new ClanChatLine(e.SteamId, e.Name ?? string.Empty, e.Message ?? string.Empty,
                    new DateTimeOffset(DateTime.SpecifyKind(e.Time, DateTimeKind.Utc), TimeSpan.Zero)));

        private void OnClanChanged(object? sender, ClanChangedEventArg e) =>
            ClanChanged?.Invoke(this,
                e.ClanInfo is { } info ? ClanProbeResult.From(ClanMapping.ToSnapshot(info)) : ClanProbeResult.NoClan);
```

Add `using RustPlusApi.Data.Events;` to the file's usings.

- [ ] **Step 8: Update the test fake**

`FakeRustSocketSource.cs` implements `IRustServerConnection`. Add to its connection class:

```csharp
    /// <summary>The probe result this fake returns; defaults to no clan.</summary>
    public ClanProbeResult ClanProbe { get; set; } = ClanProbeResult.NoClan;

    /// <summary>Messages sent to in-game clan chat through this fake.</summary>
    public List<string> SentClanMessages { get; } = [];

    /// <inheritdoc />
    public Task<ClanProbeResult> GetClanInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.FromResult(ClanProbe);

    /// <inheritdoc />
    public Task SendClanMessageAsync(string message, CancellationToken cancellationToken)
    {
        SentClanMessages.Add(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> SetClanMotdAsync(string motd, TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    /// <inheritdoc />
    public event EventHandler<ClanChatLine>? ClanMessageReceived;

    /// <inheritdoc />
    public event EventHandler<ClanProbeResult>? ClanChanged;

    /// <summary>Raises <see cref="ClanMessageReceived"/> for a test.</summary>
    /// <param name="line">The line to raise.</param>
    public void RaiseClanMessage(ClanChatLine line) => ClanMessageReceived?.Invoke(this, line);

    /// <summary>Raises <see cref="ClanChanged"/> for a test.</summary>
    /// <param name="result">The probe result to raise.</param>
    public void RaiseClanChanged(ClanProbeResult result) => ClanChanged?.Invoke(this, result);
```

Match the fake's existing naming and access modifiers; if it declares members without `///` docs (test projects still require them under `GenerateDocumentationFile`), follow whatever the surrounding file does.

- [ ] **Step 9: Build and run the full Connections suite**

Run: `dtk dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: `0 Error(s)`.

Run: `dtk dotnet test tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1`
Expected: all pass, and the total count is 8 higher than before this task.

- [ ] **Step 10: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "$(cat <<'EOF'
Map clan responses and extend the connection seam

Add ClanMapping, which is the only place clan protobuf models and
RustPlusErrorCode are interpreted, and extend IRustServerConnection with the
clan probe, clan send, MOTD write, and the two clan events. no_clan maps to a
definitive NoClan; every other failure degrades to Unavailable so a transient
fault cannot look like a departed clan.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Publish clan events from the supervisor (Connections)

**Files:**
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Modify: `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ClanSupervisorTests.cs`

**Interfaces:**
- Consumes: `ClanChatLine`, `ClanProbeResult` (Task 2); `ClanMessageReceivedEvent`, `ClanStateChangedEvent` (Task 1).
- Produces: `ConnectionSupervisor` publishes `ClanMessageReceivedEvent` and `ClanStateChangedEvent` on `IEventBus`. Sending is NOT added here — Task 6 unifies it.

Read `ConnectionSupervisorTests.cs` first — it already establishes how to stand up a supervisor over `FakeRustSocketSource` and drain the bus. Reuse that harness rather than inventing one.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Connections.Tests/ClanSupervisorTests.cs`. Model the `Build`/run harness on the existing `ConnectionSupervisorTests`; the assertions that matter are:

```csharp
// 1. On connect, the supervisor probes the clan and publishes the result.
[Fact] public async Task Publishes_a_clan_state_event_on_connect()
// Arrange the fake connection with ClanProbe = ClanProbeResult.From(snapshot).
// Assert a ClanStateChangedEvent with Status == HasClan and the same ClanId reaches the bus.

// 2. A NoClan probe still publishes, so the state service can tear down.
[Fact] public async Task Publishes_NoClan_on_connect_when_the_player_has_no_clan()

// 3. An in-game clan change is forwarded.
[Fact] public async Task Publishes_a_clan_state_event_when_the_socket_reports_a_change()
// Call fake.RaiseClanChanged(ClanProbeResult.NoClan); assert Status == NoClan reaches the bus.

// 4. Clan chat lines are published with active-player classification.
[Fact] public async Task Publishes_clan_messages_and_flags_the_active_player()
// RaiseClanMessage with SteamId == the credential's steam id => FromActivePlayer true;
// with a different SteamId => false.

// 5. Unsubscription on disconnect: after the connection loop exits, raising a clan
//    event on the fake publishes nothing further.
[Fact] public async Task Stops_publishing_clan_events_after_the_socket_closes()
```

Write these out fully against the existing harness. Do not leave them as comments.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1 --filter FullyQualifiedName~ClanSupervisorTests`
Expected: FAIL — no clan events are published.

- [ ] **Step 3: (removed)**

Nothing to do here. The supervisor's send path is unified in Task 6 as a single kind-parameterised `IChatSender`, so no clan-specific sender is added in this task. Proceed to Step 4.

- [ ] **Step 4: Subscribe, probe, and publish in the connected window**

In the method that wires `connection.TeamMessageReceived += OnTeamMessage` (around `ConnectionSupervisor.cs:551`), add local handlers next to the existing ones:

```csharp
#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<ClanChatLine> delegate shape.
        void OnClanMessage(object? sender, ClanChatLine line)
        {
            _ = PublishClanMessageAsync(key, activeSteamId, line);
        }
#pragma warning restore RCS1163

#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<ClanProbeResult> delegate shape.
        void OnClanChanged(object? sender, ClanProbeResult probe)
        {
            _ = PublishClanStateAsync(key, probe);
        }
#pragma warning restore RCS1163
```

Subscribe alongside the existing three:

```csharp
        connection.ClanMessageReceived += OnClanMessage;
        connection.ClanChanged += OnClanChanged;
```

Unsubscribe in the same `finally` block as the existing three:

```csharp
        connection.ClanMessageReceived -= OnClanMessage;
        connection.ClanChanged -= OnClanChanged;
```

Immediately after `await PrimeDevicesAsync(key, connection, ct)`, add the connect-time probe:

```csharp
        // Probe once on connect so clan state is correct after a bot restart, not only after the
        // next in-game change. An Unavailable result publishes too: the consumer preserves state.
        var clanProbe = await connection.GetClanInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
        await PublishClanStateAsync(key, clanProbe).ConfigureAwait(false);
```

- [ ] **Step 5: Add the two publish helpers**

Place these next to `PublishTeamMessageAsync`:

```csharp
    private async Task PublishClanMessageAsync((ulong Guild, Guid Server) key, ulong activeSteamId, ClanChatLine line)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var evt = new ClanMessageReceivedEvent(
                key.Guild, key.Server, line.SteamId, line.Name, line.Message, line.SteamId == activeSteamId);
            // Supervisor-wide shutdown token, not a per-connection ct: an inbound line should publish
            // regardless of one connection's reconnect cycle.
            await eventBus.PublishAsync(evt, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a publish failure must not crash the socket callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPublishClanMessageFailed(logger, ex, key.Server);
        }
    }

    private async Task PublishClanStateAsync((ulong Guild, Guid Server) key, ClanProbeResult probe)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var evt = new ClanStateChangedEvent(key.Guild, key.Server, probe.Status, probe.Snapshot);
            await eventBus.PublishAsync(evt, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a publish failure must not crash the socket callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPublishClanStateFailed(logger, ex, key.Server);
        }
    }
```

And the three log methods next to the existing `[LoggerMessage]` block:

```csharp
    [LoggerMessage(Level = LogLevel.Error, Message = "Relaying a clan message to server {ServerId} failed.")]
    private static partial void LogClanSendFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing a clan message for server {ServerId} failed.")]
    private static partial void LogPublishClanMessageFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing clan state for server {ServerId} failed.")]
    private static partial void LogPublishClanStateFailed(ILogger logger, Exception exception, Guid serverId);
```

- [ ] **Step 6: (removed)**

No new registration. Task 6 changes the existing `ITeamChatSender` registration into `IChatSender`.

- [ ] **Step 7: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1`
Expected: all pass, 5 more than after Task 2.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "$(cat <<'EOF'
Publish clan chat and clan state from the supervisor

Subscribe to the socket's clan events for the lifetime of a connected window,
probe the clan once on connect so state survives a restart, and publish both
onto the event bus.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Clan persistence (Domain, Persistence)

**Files:**
- Create: `src/RustPlusBot.Domain/Clans/ClanState.cs`
- Create: `src/RustPlusBot.Domain/Clans/ClanPlayerName.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/ClanStateConfiguration.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/ClanPlayerNameConfiguration.cs`
- Create: `src/RustPlusBot.Persistence/Clans/IClanStore.cs`
- Create: `src/RustPlusBot.Persistence/Clans/ClanStore.cs`
- Create: `src/RustPlusBot.Persistence/Clans/ClanSnapshotSerializer.cs`
- Modify: `src/RustPlusBot.Persistence/BotDbContext.cs`
- Modify: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/ClanStoreTests.cs`

**Interfaces:**
- Consumes: `ClanSnapshot` (Task 1).
- Produces:

```csharp
public interface IClanStore
{
    Task<ClanSnapshot?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
    Task SaveAsync(ulong guildId, Guid serverId, ClanSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<bool> ClearAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
    Task<bool> HasClanAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<ulong, string>> GetNamesAsync(ulong guildId, Guid serverId, IReadOnlyCollection<ulong> steamIds, CancellationToken cancellationToken = default);
    Task RecordNameAsync(ulong guildId, Guid serverId, ulong steamId, string name, CancellationToken cancellationToken = default);
}
```

`ClearAsync` returns true when a row was actually removed, so the caller can tell a real departure from a redundant teardown and avoid a pointless reconcile.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Persistence.Tests/ClanStoreTests.cs`. Follow the existing store tests in that project for how they build an in-memory/SQLite `BotDbContext` and seed a `RustServer` — reuse that helper verbatim.

```csharp
[Fact] public async Task Returns_null_when_no_clan_is_stored()
[Fact] public async Task Round_trips_a_snapshot_including_members_roles_and_invites()
// Save a snapshot with 2 roles, 3 members (one with Notes, one Online), 1 invite; read it back
// and assert every scalar and every collection element matches, including LogoHash and Score.
[Fact] public async Task Overwrites_the_previous_snapshot_on_save()
[Fact] public async Task HasClan_is_false_before_save_and_true_after()
[Fact] public async Task Clear_removes_the_row_and_reports_true_only_the_first_time()
// First ClearAsync => true; second => false.
[Fact] public async Task Deleting_the_server_cascades_to_the_clan_state()
[Fact] public async Task Records_and_reads_back_player_names()
[Fact] public async Task Name_lookup_returns_only_the_requested_ids()
[Fact] public async Task Recording_a_name_twice_updates_rather_than_duplicating()
```

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/RustPlusBot.Persistence.Tests -maxcpucount:1 --filter FullyQualifiedName~ClanStoreTests`
Expected: FAIL — `IClanStore` does not exist.

- [ ] **Step 3: Create the entities**

`src/RustPlusBot.Domain/Clans/ClanState.cs`:

```csharp
namespace RustPlusBot.Domain.Clans;

/// <summary>
/// The latest known clan snapshot for one (guild, server). Row presence is the single source of
/// truth for whether the paired player is in a clan.
/// </summary>
public sealed class ClanState
{
    /// <summary>The owning guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server id (FK to RustServer; primary key, one row per server).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The in-game clan identifier.</summary>
    public long ClanId { get; set; }

    /// <summary>The clan's display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When the clan was created (UTC).</summary>
    public DateTimeOffset Created { get; set; }

    /// <summary>Steam64 id of the clan creator.</summary>
    public ulong Creator { get; set; }

    /// <summary>The message of the day, or null when unset.</summary>
    public string? Motd { get; set; }

    /// <summary>When the MOTD was last changed (UTC), or null.</summary>
    public DateTimeOffset? MotdTimestamp { get; set; }

    /// <summary>Steam64 id of the player who last changed the MOTD, or null.</summary>
    public ulong? MotdAuthor { get; set; }

    /// <summary>Stable hash of the clan logo, or null when no logo is set.</summary>
    public string? LogoHash { get; set; }

    /// <summary>The clan colour as a packed ARGB integer, or null.</summary>
    public int? Color { get; set; }

    /// <summary>The maximum member count, or null when uncapped.</summary>
    public int? MaxMemberCount { get; set; }

    /// <summary>The clan score, or null when the server does not report one.</summary>
    public long? Score { get; set; }

    /// <summary>The clan's roles, serialized as JSON.</summary>
    public string RolesJson { get; set; } = "[]";

    /// <summary>The clan's members, serialized as JSON.</summary>
    public string MembersJson { get; set; } = "[]";

    /// <summary>The clan's pending invites, serialized as JSON.</summary>
    public string InvitesJson { get; set; } = "[]";

    /// <summary>When this snapshot was last confirmed (UTC).</summary>
    public DateTimeOffset LastSeenUtc { get; set; }
}
```

`src/RustPlusBot.Domain/Clans/ClanPlayerName.cs`:

```csharp
namespace RustPlusBot.Domain.Clans;

/// <summary>
/// A cached Steam64 id to display-name mapping. The clan API reports members by id only, so names
/// are harvested from clan chat and team snapshots, which do carry them.
/// </summary>
public sealed class ClanPlayerName
{
    /// <summary>The owning guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server id (FK to RustServer).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The Steam64 id of the player.</summary>
    public ulong SteamId { get; set; }

    /// <summary>The most recently observed display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When the name was last observed (UTC).</summary>
    public DateTimeOffset UpdatedUtc { get; set; }
}
```

- [ ] **Step 4: Create the EF configurations**

`src/RustPlusBot.Persistence/Configurations/ClanStateConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Clans;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ClanStateConfiguration : IEntityTypeConfiguration<ClanState>
{
    public void Configure(EntityTypeBuilder<ClanState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.ServerId);
        builder.Property(s => s.Name).HasMaxLength(128);
        builder.Property(s => s.Motd).HasMaxLength(1024);
        builder.Property(s => s.LogoHash).HasMaxLength(64);

        // Removing a RustServer cascades to its clan state so no orphaned snapshot lingers.
        builder.HasOne<RustServer>()
            .WithOne()
            .HasForeignKey<ClanState>(s => s.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`src/RustPlusBot.Persistence/Configurations/ClanPlayerNameConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Clans;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ClanPlayerNameConfiguration : IEntityTypeConfiguration<ClanPlayerName>
{
    public void Configure(EntityTypeBuilder<ClanPlayerName> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(n => new { n.ServerId, n.SteamId });
        builder.Property(n => n.Name).HasMaxLength(64);

        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(n => n.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 5: Register both in `BotDbContext`**

Add the `DbSet` properties alongside the existing ones:

```csharp
    /// <summary>The latest known clan snapshot per server.</summary>
    public DbSet<ClanState> ClanStates => Set<ClanState>();

    /// <summary>Cached Steam id to display-name mappings for clan members.</summary>
    public DbSet<ClanPlayerName> ClanPlayerNames => Set<ClanPlayerName>();
```

and apply both configurations in `OnModelCreating`, following the existing lines exactly:

```csharp
        modelBuilder.ApplyConfiguration(new ClanStateConfiguration());
        modelBuilder.ApplyConfiguration(new ClanPlayerNameConfiguration());
```

- [ ] **Step 6: Create the serializer**

`src/RustPlusBot.Persistence/Clans/ClanSnapshotSerializer.cs`:

```csharp
using System.Text.Json;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Persistence.Clans;

/// <summary>
/// Serializes the clan's collections to and from the JSON columns on <c>ClanState</c>. The
/// collections are only ever read and written whole (diff, then render) and nothing queries
/// across them, so three normalised tables would add migration burden for no query benefit.
/// </summary>
internal static class ClanSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes a collection to its JSON column value.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The items to serialize.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize<T>(IReadOnlyList<T> items) => JsonSerializer.Serialize(items, Options);

    /// <summary>Deserializes a JSON column value, yielding an empty list when the text is unusable.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="json">The stored JSON text.</param>
    /// <returns>The deserialized items, or an empty list.</returns>
    public static IReadOnlyList<T> Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            // A corrupted column must not crash the bot; an empty list re-reads as a full change set.
            return [];
        }
    }
}
```

- [ ] **Step 7: Create `IClanStore` and `ClanStore`**

`src/RustPlusBot.Persistence/Clans/IClanStore.cs` — the interface exactly as given in the Interfaces block above, with full `///` docs on every member and parameter.

`src/RustPlusBot.Persistence/Clans/ClanStore.cs` — a scoped `internal sealed class ClanStore(BotDbContext db) : IClanStore` that:
- `GetAsync` — `db.ClanStates.AsNoTracking().FirstOrDefaultAsync(s => s.ServerId == serverId && s.GuildId == guildId, ct)`, then rebuilds a `ClanSnapshot` using `ClanSnapshotSerializer.Deserialize<ClanRoleSnapshot>(row.RolesJson)` and the member/invite equivalents.
- `SaveAsync` — finds the tracked row or creates one, assigns every scalar plus the three JSON columns and `LastSeenUtc`, then `SaveChangesAsync`.
- `ClearAsync` — finds the row; returns false when absent; otherwise removes it, saves, and returns true.
- `HasClanAsync` — `db.ClanStates.AnyAsync(...)`.
- `GetNamesAsync` — `db.ClanPlayerNames.AsNoTracking().Where(n => n.ServerId == serverId && steamIds.Contains(n.SteamId)).ToDictionaryAsync(n => n.SteamId, n => n.Name, ct)`. Return an empty dictionary when `steamIds` is empty, without querying.
- `RecordNameAsync` — upsert by `(ServerId, SteamId)`; skip the write when the stored name already equals the new one (ordinal), so an unchanged name does not churn the DB on every chat line.

Every awaited call needs `.ConfigureAwait(false)`. Every public member needs `///` docs — put them on the interface and use `/// <inheritdoc />` on the implementation.

- [ ] **Step 8: Register the store**

In `PersistenceServiceCollectionExtensions.cs`, after `services.AddScoped<IWipeBaselineStore, WipeBaselineStore>();`:

```csharp
        services.AddScoped<IClanStore, ClanStore>();
```

- [ ] **Step 9: Run the tests**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests -maxcpucount:1`
Expected: all pass, 9 more than before.

- [ ] **Step 10: Create the migration**

```bash
dotnet build RustPlusBot.slnx -maxcpucount:1
dotnet ef migrations add ClanSupport \
  --project src/RustPlusBot.Persistence \
  --startup-project src/RustPlusBot.Host
```

Expected: a new `Migrations/<timestamp>_ClanSupport.cs` plus an updated `BotDbContextModelSnapshot.cs`. Open the migration and confirm it creates exactly two tables and no others — if it contains unrelated changes, the model snapshot was stale; investigate before continuing.

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: `0 Error(s)`.

- [ ] **Step 11: Commit**

```bash
git add src/RustPlusBot.Domain/Clans src/RustPlusBot.Persistence tests/RustPlusBot.Persistence.Tests
git commit -m "$(cat <<'EOF'
Persist clan state and cached player names

Add the ClanState and ClanPlayerName entities, their configurations, the
scoped IClanStore, and the ClanSupport migration. Members, roles and invites
are stored as JSON because they are only ever read and written whole.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Workspace capability seam, pinning, and clan channel specs

The reconciler currently provisions every declared channel unconditionally. This task adds a generic capability gate — reusable beyond clans — plus message pinning, then declares the clan channels and the clan-chat locator.

**Files:**
- Create: `src/RustPlusBot.Features.Workspace/Registry/IWorkspaceCapabilityProvider.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/ClanChatChannelLocator.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Registry/ChannelSpec.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Registry/MessageSpec.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Registry/IWorkspaceRegistry.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Registry/WorkspaceRegistry.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Reconciler/WorkspaceReconciler.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Gateway/IWorkspaceGateway.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Gateway/DiscordWorkspaceGateway.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Reconciler/ServerInfoRefresher.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/CapabilityGatedChannelTests.cs`
- Modify: the Workspace tests' fake gateway (find it under `tests/RustPlusBot.Features.Workspace.Tests/` — it implements `IWorkspaceGateway`)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `internal interface IWorkspaceCapabilityProvider { string Capability { get; } ValueTask<bool> IsAvailableAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken); }`
  - `ChannelSpec` gains a trailing `string? Capability = null`.
  - `MessageSpec` gains a trailing `bool Pinned = false`.
  - `IWorkspaceRegistry` gains `ValueTask<bool> IsCapabilityAvailableAsync(string capability, ulong guildId, Guid? serverId, CancellationToken cancellationToken)`.
  - `IWorkspaceGateway` gains `Task PinMessageAsync(ulong guildId, ulong channelId, ulong messageId, CancellationToken cancellationToken)`.
  - `WorkspaceChannelKeys.ServerClanChat = "clanchat"`, `WorkspaceChannelKeys.ServerClanInfo = "claninfo"`.
  - `WorkspaceMessageKeys.ClanOverview = "clan.overview"`, `ClanRoster = "clan.roster"`, `ClanInvites = "clan.invites"`.
  - `WorkspaceCapabilities.Clan = "clan"`.
  - `internal sealed class ClanChatChannelLocator : CachingChannelLocator` over `WorkspaceChannelKeys.ServerClanChat`. It gets its `IChatChannelLocator` interface and its DI registration in Task 6, which unifies the locator seam; here it is only the class.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Workspace.Tests/CapabilityGatedChannelTests.cs`. Read the existing reconciler tests in that project first and reuse their fake gateway / in-memory store harness.

```csharp
[Fact] public async Task Creates_a_capability_gated_channel_when_the_capability_is_available()
// Registry has a spec with Capability = "clan"; a provider returns true.
// Assert the gateway created a channel for that key and the store has a row.

[Fact] public async Task Skips_and_deletes_a_capability_gated_channel_when_unavailable()
// Provider returns false and a ProvisionedChannel row already exists.
// Assert gateway.DeleteChannelAsync was called with that channel id AND the store row is gone.

[Fact] public async Task Does_not_create_a_capability_gated_channel_that_was_never_provisioned()
// Provider false, no existing row => no create, no delete, no throw.

[Fact] public async Task Treats_a_capability_with_no_registered_provider_as_unavailable()
// Spec has Capability = "ghost"; no provider registered.
// Assert the channel is not created.

[Fact] public async Task Leaves_ungated_channels_untouched()
// A spec with Capability == null is created exactly as before.

[Fact] public async Task Pins_a_pinned_message_only_when_it_is_newly_posted()
// First reconcile: PinMessageAsync called once for the pinned spec.
// Second reconcile (message still live, so it is edited): PinMessageAsync NOT called again.

[Fact] public async Task Does_not_pin_messages_that_are_not_marked_pinned()

[Fact] public async Task A_failed_pin_does_not_fail_the_reconcile()
// Fake gateway throws from PinMessageAsync; assert the message is still saved to the store.
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests -maxcpucount:1 --filter FullyQualifiedName~CapabilityGatedChannelTests`
Expected: FAIL — `IWorkspaceCapabilityProvider` does not exist.

- [ ] **Step 3: Add the capability seam**

`src/RustPlusBot.Features.Workspace/Registry/IWorkspaceCapabilityProvider.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>
/// Answers whether an optional workspace capability currently applies to a scope. A
/// <see cref="ChannelSpec"/> naming a capability is provisioned only while its provider reports
/// available, and its channel is deleted when it does not.
/// </summary>
internal interface IWorkspaceCapabilityProvider
{
    /// <summary>The capability name this provider answers for (matches <see cref="ChannelSpec.Capability"/>).</summary>
    string Capability { get; }

    /// <summary>Reports whether the capability currently applies to the given scope.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id, or null for the global scope.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when the capability's channels should exist.</returns>
    ValueTask<bool> IsAvailableAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken);
}
```

Add the capability-name constants to `WorkspaceKeys.cs`:

```csharp
/// <summary>Stable capability names gating optional workspace channels.</summary>
internal static class WorkspaceCapabilities
{
    /// <summary>Gates the per-server clan channels; available only while the paired player is in a clan.</summary>
    public const string Clan = "clan";
}
```

- [ ] **Step 4: Extend the two spec records**

`ChannelSpec.cs` — append the parameter and its doc:

```csharp
/// <param name="Capability">Optional capability gating this channel; null means always provisioned.</param>
internal sealed record ChannelSpec(
    WorkspaceScope Scope,
    string Key,
    string NameKey,
    ChannelPermissionProfile Permissions,
    int Order,
    string? Capability = null);
```

`MessageSpec.cs`:

```csharp
/// <param name="Pinned">True to pin the message when it is first posted, so a channel that also
/// carries transient messages keeps this one reachable from the pin bar.</param>
internal sealed record MessageSpec(WorkspaceScope Scope, string Key, string ChannelKey, bool Pinned = false);
```

- [ ] **Step 5: Aggregate providers in the registry**

`IWorkspaceRegistry` gains:

```csharp
    /// <summary>
    /// Reports whether a named capability currently applies. An unknown capability — one with no
    /// registered provider — is unavailable, so a feature the host did not compose leaves no
    /// orphaned channels behind.
    /// </summary>
    /// <param name="capability">The capability name.</param>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id, or null for the global scope.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when the capability's channels should exist.</returns>
    ValueTask<bool> IsCapabilityAvailableAsync(string capability,
        ulong guildId,
        Guid? serverId,
        CancellationToken cancellationToken);
```

`WorkspaceRegistry` — take the providers and index them:

```csharp
internal sealed class WorkspaceRegistry(
    IEnumerable<IChannelSpecProvider> channelProviders,
    IEnumerable<IMessageSpecProvider> messageProviders,
    IEnumerable<IWorkspaceCapabilityProvider> capabilityProviders) : IWorkspaceRegistry
{
    private readonly List<ChannelSpec> _channels = [.. channelProviders.SelectMany(p => p.GetChannelSpecs())];
    private readonly List<MessageSpec> _messages = [.. messageProviders.SelectMany(p => p.GetMessageSpecs())];

    private readonly Dictionary<string, IWorkspaceCapabilityProvider> _capabilities =
        capabilityProviders.ToDictionary(p => p.Capability, StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<bool> IsCapabilityAvailableAsync(string capability,
        ulong guildId,
        Guid? serverId,
        CancellationToken cancellationToken) =>
        _capabilities.TryGetValue(capability, out var provider)
            ? provider.IsAvailableAsync(guildId, serverId, cancellationToken)
            : ValueTask.FromResult(false);

    // ... existing GetChannelSpecs / GetMessageSpecs unchanged
}
```

`WorkspaceRegistry` is registered as a singleton but `ClanCapabilityProvider` needs a scoped store, so the provider resolves its store per call from `IServiceScopeFactory` (Task 10). Do not change the registry's lifetime.

- [ ] **Step 6: Gate creation and delete on unavailability in the reconciler**

In `EnsureChannelsAsync`, replace the body of the `foreach (var spec in specs)` loop's opening with a gate, and track which specs were gated off:

```csharp
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var specs = backends.Registry.GetChannelSpecs(scope);
        var gatedOff = new List<string>();

        foreach (var spec in specs)
        {
            if (spec.Capability is { } capability &&
                !await backends.Registry
                    .IsCapabilityAvailableAsync(capability, guildId, serverId, cancellationToken)
                    .ConfigureAwait(false))
            {
                gatedOff.Add(spec.Key);
                continue;
            }

            var name = localizer.Get(spec.NameKey, culture);
            // ... existing body unchanged
        }
```

Then, after the loop and before the orphan-retention logging, delete the gated-off channels:

```csharp
        // A capability that has gone away is an explicit removal, distinct from a spec merely
        // disappearing from the registry (which is retained, below).
        foreach (var key in gatedOff)
        {
            if (!existing.TryGetValue(key, out var stale))
            {
                continue;
            }

            await backends.Gateway.DeleteChannelAsync(guildId, stale.DiscordChannelId, cancellationToken)
                .ConfigureAwait(false);
            await backends.Store.DeleteChannelAsync(guildId, serverId, key, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Removed channel '{Key}' for guild {GuildId}: its capability is no longer available.", key, guildId);
        }
```

Finally, exclude the gated-off keys from the orphan-retention log so a removed channel is not also reported as retained:

```csharp
        var registryKeys = specs.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
```

is already correct — gated-off keys are still in `specs`, so they are not treated as orphans. No change needed there.

- [ ] **Step 7: Add `DeleteChannelAsync` to `IWorkspaceStore`**

`IWorkspaceStore` has `DeleteScopeAsync` but no single-channel delete. Add to `src/RustPlusBot.Persistence/Workspace/IWorkspaceStore.cs`:

```csharp
    /// <summary>Deletes one provisioned channel row and any messages anchored in it.</summary>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id, or null for the global scope.</param>
    /// <param name="channelKey">The stable channel key to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task DeleteChannelAsync(ulong guildId,
        Guid? serverId,
        string channelKey,
        CancellationToken cancellationToken = default);
```

Implement it in `WorkspaceStore`: remove the matching `ProvisionedChannel`, then remove every `ProvisionedMessage` for the scope whose `DiscordChannelId` equals the removed channel's id, then `SaveChangesAsync`. Deleting the messages matters — a stale `ProvisionedMessage` pointing at a deleted channel would make a later reconcile try to edit a message in a channel that no longer exists.

Add a persistence test for it in the existing workspace store test file:

```csharp
[Fact] public async Task Deleting_a_channel_also_removes_its_anchored_messages()
```

- [ ] **Step 8: Add pinning to the gateway**

`IWorkspaceGateway`:

```csharp
    /// <summary>Pins a message. A no-op if it is already pinned or already gone.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="channelId">The snowflake ID of the channel containing the message.</param>
    /// <param name="messageId">The snowflake ID of the message to pin.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task PinMessageAsync(ulong guildId, ulong channelId, ulong messageId, CancellationToken cancellationToken);
```

`DiscordWorkspaceGateway` — implement it following the file's existing channel/message resolution helpers:

```csharp
    /// <inheritdoc />
    public async Task PinMessageAsync(ulong guildId,
        ulong channelId,
        ulong messageId,
        CancellationToken cancellationToken)
    {
        if (client.GetGuild(guildId)?.GetTextChannel(channelId) is not { } channel)
        {
            return;
        }

        if (await channel.GetMessageAsync(messageId).ConfigureAwait(false) is IUserMessage message)
        {
            await message.PinAsync().ConfigureAwait(false);
        }
    }
```

Match the surrounding code's actual client-access idiom rather than copying this verbatim if the file resolves channels differently.

- [ ] **Step 9: Pin newly posted messages in `EnsureMessagesAsync`**

In the post/edit block, capture whether the message was newly posted and pin it:

```csharp
                ulong messageId;
                var newlyPosted = false;
                if (item.LiveId is { } liveId)
                {
                    await backends.Gateway
                        .EditMessageAsync(guildId, channelId, liveId, item.Payload, cancellationToken)
                        .ConfigureAwait(false);
                    messageId = liveId;
                }
                else
                {
                    messageId = await backends.Gateway
                        .PostMessageAsync(guildId, channelId, item.Payload, cancellationToken)
                        .ConfigureAwait(false);
                    newlyPosted = true;
                }

                // Pin only on first post: pinning is idempotent but costs an API call, and a
                // re-posted message (declaration-order repair) also lands here as newly posted.
                if (newlyPosted && item.Spec.Pinned)
                {
                    try
                    {
                        await backends.Gateway.PinMessageAsync(guildId, channelId, messageId, cancellationToken)
                            .ConfigureAwait(false);
                    }
#pragma warning disable CA1031 // Broad catch: an unpinned embed is still correct; never fail a reconcile over it.
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        logger.LogWarning(ex, "Pinning message '{Key}' in guild {GuildId} failed.", item.Spec.Key,
                            guildId);
                    }
                }
```

- [ ] **Step 10: Declare the clan keys, specs, and locator**

`WorkspaceKeys.cs` — add to `WorkspaceChannelKeys`:

```csharp
    /// <summary>Key for the per-server #clanchat channel (provisioned only while a clan exists).</summary>
    public const string ServerClanChat = "clanchat";

    /// <summary>Key for the per-server #claninfo channel (provisioned only while a clan exists).</summary>
    public const string ServerClanInfo = "claninfo";
```

and to `WorkspaceMessageKeys`:

```csharp
    /// <summary>Key for the pinned clan overview embed. Rendered by Features.Clans.</summary>
    public const string ClanOverview = "clan.overview";

    /// <summary>Key for the pinned clan roster embed. Rendered by Features.Clans.</summary>
    public const string ClanRoster = "clan.roster";

    /// <summary>Key for the pinned clan invites embed. Rendered by Features.Clans.</summary>
    public const string ClanInvites = "clan.invites";
```

`ServerWorkspaceSpecProvider.cs` — insert the two channels after teamchat and renumber the rest:

```csharp
    public IEnumerable<ChannelSpec> GetChannelSpecs() =>
    [
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerInfo, "channel.info.name",
            ChannelPermissionProfile.ReadOnly, 0),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerTeamChat, "channel.teamchat.name",
            ChannelPermissionProfile.Interactive, 1),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerClanChat, "channel.clanchat.name",
            ChannelPermissionProfile.Interactive, 2, WorkspaceCapabilities.Clan),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerClanInfo, "channel.claninfo.name",
            ChannelPermissionProfile.ReadOnly, 3, WorkspaceCapabilities.Clan),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerEvents, "channel.events.name",
            ChannelPermissionProfile.ReadOnly, 4),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerMap, "channel.map.name",
            ChannelPermissionProfile.ReadOnly, 5),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerSwitches, "channel.switches.name",
            ChannelPermissionProfile.Interactive, 6),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerAlarms, "channel.alarms.name",
            ChannelPermissionProfile.Interactive, 7),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerStorageMonitors, "channel.storagemonitors.name",
            ChannelPermissionProfile.Interactive, 8),
    ];
```

and add the three pinned messages to `GetMessageSpecs()`, after the existing entries:

```csharp
        // #claninfo also carries a transient change feed, so the anchored embeds are pinned to stay
        // reachable once the feed pushes them up.
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ClanOverview, WorkspaceChannelKeys.ServerClanInfo, true),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ClanRoster, WorkspaceChannelKeys.ServerClanInfo, true),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ClanInvites, WorkspaceChannelKeys.ServerClanInfo, true),
```

`ClanChatChannelLocator.cs` — a `CachingChannelLocator` subclass over `WorkspaceChannelKeys.ServerClanChat` with the same `ResolveAsync` reverse lookup `TeamChatChannelLocator` has. Do **not** give it a `ClanChat`-specific interface and do **not** register it yet: Task 6 introduces the shared `IChatChannelLocator` that both locators implement, and registers them as a collection. Leaving it unregistered for one task is intentional and the build stays green.

- [ ] **Step 11: Refresh the clan embeds too**

`ServerInfoRefresher.cs` — append the three keys to the `Keys` array:

```csharp
    private static readonly string[] Keys =
    [
        WorkspaceMessageKeys.ServerInfo,
        WorkspaceMessageKeys.ServerEvents,
        WorkspaceMessageKeys.ServerTeam,
        WorkspaceMessageKeys.ClanOverview,
        WorkspaceMessageKeys.ClanRoster,
        WorkspaceMessageKeys.ClanInvites,
    ];
```

The existing `if (!_renderers.TryGetValue(key, out var renderer)) continue;` already makes these no-ops when `Features.Clans` is not composed, and the existing missing-message path already falls back to a full reconcile.

- [ ] **Step 12: Update the fake gateway and run the suite**

Add `PinMessageAsync` to the Workspace tests' fake gateway, recording `(channelId, messageId)` calls in a public list so the pin tests can assert on it, plus a settable `bool ThrowOnPin` for the failure test.

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: `0 Error(s)`.

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests tests/RustPlusBot.Persistence.Tests -maxcpucount:1`
Expected: all pass, 9 more than before (8 capability/pin + 1 store).

- [ ] **Step 13: Commit**

```bash
git add src/RustPlusBot.Features.Workspace src/RustPlusBot.Persistence \
        tests/RustPlusBot.Features.Workspace.Tests tests/RustPlusBot.Persistence.Tests
git commit -m "$(cat <<'EOF'
Gate workspace channels on capabilities and support pinning

Add a generic IWorkspaceCapabilityProvider seam so a ChannelSpec can name a
capability: the reconciler provisions it only while available and deletes the
channel and its anchored messages when it is not. A capability with no
registered provider counts as unavailable. Also add MessageSpec.Pinned, pinned
on first post only, and declare the two clan channels and three clan embeds.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Unify the chat sender and locator seams

The clan bridge is NOT a copy of the team bridge — one kind-driven bridge serves both. This task unifies the three seams that currently hardcode "team"; Task 7 generalises the behaviour on top of them. Splitting them keeps each task at a green suite.

**Files:**
- Create: `src/RustPlusBot.Abstractions/Chat/ChatChannelKind.cs`
- Create: `src/RustPlusBot.Abstractions/Chat/RelayDedupBuffer.cs`
- Delete: `src/RustPlusBot.Features.Chat/Relaying/RelayDedupBuffer.cs`
- Create: `src/RustPlusBot.Features.Connections/Listening/IChatSender.cs`
- Delete: `src/RustPlusBot.Features.Connections/Listening/ITeamChatSender.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/BotTeamChatSender.cs`, `IBotTeamChatSender.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Modify: `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/IChatChannelLocator.cs`
- Delete: `src/RustPlusBot.Features.Workspace/Locating/ITeamChatChannelLocator.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Locating/TeamChatChannelLocator.cs`, `ClanChatChannelLocator.cs`, `WorkspaceServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Features.Chat/Relaying/TeamChatRelay.cs`, `Inbound/TeamChatInboundProcessor.cs`, `ChatServiceCollectionExtensions.cs`
- Modify: the Chat and Connections test files that reference the removed types

**Interfaces:**
- Consumes: `IClock` (existing); `ClanChatLine` and the clan socket members (Task 2).
- Produces:

```csharp
namespace RustPlusBot.Abstractions.Chat;
public enum ChatChannelKind { Team = 0, Clan = 1 }

public sealed class RelayDedupBuffer(IClock clock)
{
    public void Record(ChatChannelKind kind, (ulong Guild, Guid Server) key, string text);
    public bool TryConsume(ChatChannelKind kind, (ulong Guild, Guid Server) key, string text);
}

namespace RustPlusBot.Features.Connections.Listening;
public enum ChatSendResult { Sent = 0, NotConnected = 1, Failed = 2 }

public interface IChatSender
{
    Task<ChatSendResult> SendAsync(ChatChannelKind kind, ulong guildId, Guid serverId, string message, CancellationToken cancellationToken);
}

namespace RustPlusBot.Features.Workspace.Locating;
public interface IChatChannelLocator
{
    ChatChannelKind Kind { get; }
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
    Task<(ulong GuildId, Guid ServerId)?> ResolveAsync(ulong channelId, CancellationToken cancellationToken);
}
```

`ITeamChatSender`, `TeamChatSendResult` and `ITeamChatChannelLocator` are **removed**, not kept as aliases. Two names for one concept is how the duplication returns.

Because `IChatSender.SendAsync` now takes the kind as a parameter, the supervisor implements it once as a normal (non-explicit) member — no `IClanChatSender`, and none of the explicit-implementation awkwardness a second same-shaped interface would force.

- [ ] **Step 1: Write the failing tests**

Update `tests/RustPlusBot.Features.Chat.Tests/RelayDedupBufferTests.cs` for the new signature, keeping every existing case, and add:

```csharp
    [Fact]
    public void A_clan_echo_does_not_consume_an_identical_team_entry()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var buffer = new RelayDedupBuffer(clock);
        var key = (1UL, Guid.NewGuid());

        buffer.Record(ChatChannelKind.Team, key, "[dave] hello");

        Assert.False(buffer.TryConsume(ChatChannelKind.Clan, key, "[dave] hello"));
        Assert.True(buffer.TryConsume(ChatChannelKind.Team, key, "[dave] hello"));
    }

    [Fact]
    public void Consumes_a_clan_entry_recorded_for_the_same_key()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var buffer = new RelayDedupBuffer(clock);
        var key = (1UL, Guid.NewGuid());

        buffer.Record(ChatChannelKind.Clan, key, "[dave] hello");

        Assert.True(buffer.TryConsume(ChatChannelKind.Clan, key, "[dave] hello"));
    }
```

Use whatever fake clock that file already uses. Add to `tests/RustPlusBot.Features.Connections.Tests/ClanSupervisorTests.cs` (created in Task 3):

```csharp
    [Fact]
    public async Task Routes_a_team_send_to_team_chat_and_a_clan_send_to_clan_chat()
    {
        // One IChatSender, two destinations: assert the kind selects the right socket call and
        // that neither leaks into the other's buffer.
    }
```

Write it fully against the Task 3 harness, asserting `fake.SentTeamMessages` and `fake.SentClanMessages` independently.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Chat.Tests tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1`
Expected: FAIL — compile errors; `Record` takes two arguments and `IChatSender` does not exist.

- [ ] **Step 3: Move and re-key the dedup buffer**

`src/RustPlusBot.Abstractions/Chat/ChatChannelKind.cs`:

```csharp
namespace RustPlusBot.Abstractions.Chat;

/// <summary>Which in-game chat channel a relayed line belongs to.</summary>
public enum ChatChannelKind
{
    /// <summary>In-game team chat.</summary>
    Team = 0,

    /// <summary>In-game clan chat.</summary>
    Clan = 1,
}
```

`src/RustPlusBot.Abstractions/Chat/RelayDedupBuffer.cs` — the existing implementation moved verbatim, with three changes: namespace becomes `RustPlusBot.Abstractions.Chat`; the class becomes `public sealed`; the dictionary key becomes `(ChatChannelKind Kind, ulong Guild, Guid Server)` and both methods take `ChatChannelKind kind` first. Update the class doc to explain that the kind is part of the key so an identical line relayed to both channels within the TTL cannot cross-cancel. Delete the old file.

- [ ] **Step 4: Unify the sender**

`src/RustPlusBot.Features.Connections/Listening/IChatSender.cs` — `ChatSendResult` and `IChatSender` exactly as in the Interfaces block, with full `///` docs. Delete `ITeamChatSender.cs`.

On `ConnectionSupervisor`, replace `ITeamChatSender` in the base list with `IChatSender` and rewrite the existing `SendAsync` to dispatch on kind:

```csharp
    /// <inheritdoc />
    public async Task<ChatSendResult> SendAsync(
        ChatChannelKind kind,
        ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return ChatSendResult.NotConnected;
        }

        try
        {
            switch (kind)
            {
                case ChatChannelKind.Team:
                    await live.Connection.SendTeamMessageAsync(message, cancellationToken).ConfigureAwait(false);
                    break;
                case ChatChannelKind.Clan:
                    await live.Connection.SendClanMessageAsync(message, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    return ChatSendResult.Failed;
            }

            return ChatSendResult.Sent;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a failed relay send must not crash the caller; report Failed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, ex, serverId);
            return ChatSendResult.Failed;
        }
    }
```

Update the DI registration to `services.AddSingleton<IChatSender>(sp => sp.GetRequiredService<ConnectionSupervisor>());`, the disposal comment that names `ITeamChatSender`, and `BotTeamChatSender` to take `IChatSender` and pass `ChatChannelKind.Team`.

- [ ] **Step 5: Unify the locator**

`src/RustPlusBot.Features.Workspace/Locating/IChatChannelLocator.cs` — as in the Interfaces block, with docs written generically ("the per-server chat channel for this kind"), not team-specific. Delete `ITeamChatChannelLocator.cs`.

`TeamChatChannelLocator` implements `IChatChannelLocator` with `public ChatChannelKind Kind => ChatChannelKind.Team;`. `ClanChatChannelLocator` (created in Task 5) does the same with `Clan`, and gains the `ResolveAsync` override it already has.

Registration changes to a collection so the bridge can index by kind:

```csharp
        services.AddSingleton<IChatChannelLocator, TeamChatChannelLocator>();
        services.AddSingleton<IChatChannelLocator, ClanChatChannelLocator>();
```

`IClanInfoChannelLocator` (Task 11) is unaffected — it is not a chat channel and keeps its own single-purpose interface.

- [ ] **Step 6: Update the existing team bridge call sites**

`TeamChatRelay` — take `IEnumerable<IChatChannelLocator>` is NOT needed yet; for this task simply resolve the team locator by filtering the injected collection in the constructor:

```csharp
internal sealed class TeamChatRelay(
    IEnumerable<IChatChannelLocator> locators,
    ITeamChatWebhookPoster poster,
    RelayDedupBuffer dedup,
    IServiceScopeFactory scopeFactory)
{
    private readonly IChatChannelLocator _locator = locators.Single(l => l.Kind == ChatChannelKind.Team);
```

and pass `ChatChannelKind.Team` to `dedup.TryConsume`. `TeamChatInboundProcessor` does the same, passes `ChatChannelKind.Team` to `dedup.Record`, calls `sender.SendAsync(ChatChannelKind.Team, ...)`, and compares against `ChatSendResult.Sent`.

This is deliberately a minimal adaptation — Task 7 replaces both classes with kind-driven ones. Do not generalise them here; keeping this task's diff to seam changes is what makes it reviewable.

`ChatServiceCollectionExtensions` — the `RelayDedupBuffer` registration stays but its `using` moves to `RustPlusBot.Abstractions.Chat`.

- [ ] **Step 7: Run the affected suites**

Run: `dtk dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: `0 Error(s)`.

Run: `dtk dotnet test tests/RustPlusBot.Features.Chat.Tests tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1`
Expected: all pass. Chat is 2 higher than before; Connections is 1 higher.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Abstractions src/RustPlusBot.Features.Connections \
        src/RustPlusBot.Features.Workspace src/RustPlusBot.Features.Chat \
        tests/RustPlusBot.Features.Chat.Tests tests/RustPlusBot.Features.Connections.Tests
git commit -m "$(cat <<'EOF'
Unify the chat sender, locator and dedup seams across channel kinds

Replace ITeamChatSender and ITeamChatChannelLocator with kind-parameterised
IChatSender and IChatChannelLocator, and move RelayDedupBuffer to Abstractions
keyed by channel kind so team and clan echoes cannot cross-cancel. One seam per
concept, so the clan bridge does not need a parallel set.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Generalise the chat bridge to serve both channels

One bridge, driven by `ChatChannelKind`. `Features.Clans` gets **no** chat bridge — `Features.Chat` owns both, because relaying in-game chat to Discord is one concern regardless of which in-game channel it came from.

**Files:**
- Rename: `Relaying/TeamChatRelay.cs` → `Relaying/ChatRelay.cs`
- Rename: `Inbound/TeamChatInboundProcessor.cs` → `Inbound/ChatInboundProcessor.cs`
- Rename: `Webhooks/ITeamChatWebhookPoster.cs` → `Webhooks/IChatWebhookPoster.cs`, `DiscordTeamChatWebhookPoster.cs` → `DiscordChatWebhookPoster.cs`
- Create: `src/RustPlusBot.Features.Chat/Relaying/RelayedChatLine.cs`
- Modify: `src/RustPlusBot.Features.Chat/Hosting/ChatHostedService.cs`
- Modify: `src/RustPlusBot.Features.Chat/ChatServiceCollectionExtensions.cs`
- Modify/rename the corresponding test files

**Interfaces:**
- Consumes: `ChatChannelKind`, `RelayDedupBuffer`, `IChatSender`, `IChatChannelLocator` (Task 6); `ClanMessageReceivedEvent` (Task 1).
- Produces:

```csharp
/// One received in-game chat line, normalised across channel kinds.
internal sealed record RelayedChatLine(
    ChatChannelKind Kind, ulong GuildId, Guid ServerId,
    string SenderName, string Message, bool FromActivePlayer);

internal sealed class ChatRelay
{
    public Task RelayAsync(RelayedChatLine line, CancellationToken cancellationToken);
}

internal sealed class ChatInboundProcessor
{
    public Task<InboundOutcome> ProcessAsync(InboundMessage message, CancellationToken cancellationToken);
}

public interface IChatWebhookPoster
{
    Task PostAsync(ChatChannelKind kind, ulong channelId, string username, string message, CancellationToken cancellationToken);
}
```

`TeamMessageReceivedEvent` and `ClanMessageReceivedEvent` stay as separate bus types — `Features.Commands` consumes the team one, and merging them would drag a working feature into this refactor. `ChatHostedService` adapts each into `RelayedChatLine` in three lines.

**Kind routing on the inbound path:** `ChatInboundProcessor` no longer knows its kind up front. It asks each registered `IChatChannelLocator` to resolve the incoming channel id; the one that matches supplies both the `(guild, server)` and the `Kind`. A channel that no locator claims is ignored.

**Webhook naming:** `DiscordChatWebhookPoster` derives the name from the kind — `"RustPlusBot TeamChat"` / `"RustPlusBot ClanChat"`. Keep the existing team name byte-for-byte: changing it would orphan every webhook already provisioned in live guilds and silently create duplicates. Cache clients per `(kind, channelId)`.

- [ ] **Step 1: Write the failing tests**

Rename `TeamChatRelayTests.cs` → `ChatRelayTests.cs` and `TeamChatInboundProcessorTests.cs` → `ChatInboundProcessorTests.cs`, converting each existing test to the new signature and making the kind a parameter. Every behavioural test becomes a `[Theory]` over both kinds, because both must behave identically:

```csharp
    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Posts_a_normal_message_via_webhook(ChatChannelKind kind)

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Drops_a_bot_prefixed_line_from_the_active_player(ChatChannelKind kind)

    [Theory] // ... and likewise for:
    // Keeps_a_bot_prefixed_line_from_another_player
    // Drops_our_own_echo
    // Drops_a_command_invocation
    // Ignores_leading_whitespace_when_matching_the_command_prefix
    // Does_nothing_when_the_channel_is_not_provisioned
```

Plus these cross-kind facts, which are the whole point of the unification:

```csharp
    [Fact] public async Task A_team_dedup_entry_does_not_suppress_a_clan_line()
    [Fact] public async Task A_clan_dedup_entry_does_not_suppress_a_team_line()
    [Fact] public async Task Posts_a_clan_line_to_the_clan_channel_not_the_team_channel()
    [Fact] public async Task Routes_an_inbound_message_by_which_locator_claims_the_channel()
    [Fact] public async Task Ignores_an_inbound_message_in_a_channel_no_locator_claims()
    [Fact] public async Task Uses_the_clan_webhook_name_for_clan_lines()
```

And for the inbound processor, converted to `[Theory]` over both kinds: ignores bots/webhooks, ignores empty content, mute gate without recording dedup, records dedup before sending, formats `"[dave] hello"`, `Failed` on a failed send, `Failed` on `NotConnected`.

`ChatHostedServiceTests` gains:

```csharp
    [Fact] public async Task Relays_a_clan_message_event()
    [Fact] public async Task Relays_a_team_message_event()
```

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/RustPlusBot.Features.Chat.Tests -maxcpucount:1`
Expected: FAIL — `ChatRelay` and `RelayedChatLine` do not exist.

- [ ] **Step 3: Generalise the poster**

Rename the interface and class. `PostAsync` gains a leading `ChatChannelKind kind`. Replace the `WebhookName` constant with:

```csharp
    /// <summary>
    /// Webhook name per channel kind. These strings are load-bearing: the poster re-discovers its
    /// webhook by name on restart, so changing one orphans every webhook already created in live
    /// guilds and silently creates a duplicate alongside it.
    /// </summary>
    /// <param name="kind">The channel kind.</param>
    /// <returns>The webhook name to find or create.</returns>
    private static string WebhookNameFor(ChatChannelKind kind) => kind switch
    {
        ChatChannelKind.Clan => "RustPlusBot ClanChat",
        _ => "RustPlusBot TeamChat",
    };
```

Key the client cache on `(ChatChannelKind Kind, ulong ChannelId)`.

- [ ] **Step 4: Generalise the relay**

`Relaying/RelayedChatLine.cs` as in the Interfaces block, with full `///` docs.

`Relaying/ChatRelay.cs` — the existing `TeamChatRelay` body with `evt` replaced by `line`, the locator chosen by `line.Kind`, and the kind threaded into the dedup and poster calls:

```csharp
internal sealed class ChatRelay(
    IEnumerable<IChatChannelLocator> locators,
    IChatWebhookPoster poster,
    RelayDedupBuffer dedup,
    IServiceScopeFactory scopeFactory)
{
    private readonly Dictionary<ChatChannelKind, IChatChannelLocator> _locators =
        locators.ToDictionary(l => l.Kind);

    /// <summary>Relays one received in-game chat line into its Discord channel.</summary>
    /// <param name="line">The received line.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the line has been relayed or dropped.</returns>
    public async Task RelayAsync(RelayedChatLine line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.FromActivePlayer && line.Message.StartsWith(BotTeamChat.Prefix, StringComparison.Ordinal))
        {
            return; // Bot-originated line echoing back; the channel carries only human discussion.
        }

        if (line.FromActivePlayer &&
            dedup.TryConsume(line.Kind, (line.GuildId, line.ServerId), line.Message))
        {
            return; // Our own relayed line echoing back; do not re-post.
        }

        if (!_locators.TryGetValue(line.Kind, out var locator))
        {
            return;
        }

        // The locator is an in-memory cache, so resolve the channel first: unmapped servers exit
        // before the per-message prefix query below.
        var channelId = await locator.GetChannelIdAsync(line.GuildId, line.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is null)
        {
            return;
        }

        // A command invocation gets its reply in game; the bare trigger line is noise in Discord.
        var prefix = await GetCommandPrefixAsync(line.GuildId, line.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(prefix) &&
            line.Message.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        await poster.PostAsync(line.Kind, channelId.Value, line.SenderName, line.Message, cancellationToken)
            .ConfigureAwait(false);
    }

    // GetCommandPrefixAsync unchanged from TeamChatRelay.
}
```

`BotTeamChat.Prefix` is the bot's single marker for its own in-game output across both channels, not a team-specific constant. Leave its name alone — renaming it is churn in unrelated files.

- [ ] **Step 5: Generalise the inbound processor**

`Inbound/ChatInboundProcessor.cs` — resolve the kind from whichever locator claims the channel:

```csharp
        (ulong GuildId, Guid ServerId)? target = null;
        var kind = ChatChannelKind.Team;
        foreach (var locator in locators)
        {
            if (await locator.ResolveAsync(message.ChannelId, cancellationToken).ConfigureAwait(false) is { } hit)
            {
                target = hit;
                kind = locator.Kind;
                break;
            }
        }

        if (target is not { } t)
        {
            return InboundOutcome.Ignored;
        }
```

then the existing mute gate, `dedup.Record(kind, key, text)`, `sender.SendAsync(kind, t.GuildId, t.ServerId, text, cancellationToken)`, and `result == ChatSendResult.Sent ? InboundOutcome.Sent : InboundOutcome.Failed`.

- [ ] **Step 6: Subscribe to both events**

`ChatHostedService` — add a second consumer loop, identical in shape to the first, and adapt both event types:

```csharp
        _teamLoop = Task.Run(() => ConsumeTeamMessagesAsync(_cts.Token), CancellationToken.None);
        _clanLoop = Task.Run(() => ConsumeClanMessagesAsync(_cts.Token), CancellationToken.None);
```

with the bodies mapping to `RelayedChatLine`:

```csharp
                await relay.RelayAsync(
                    new RelayedChatLine(ChatChannelKind.Team, evt.GuildId, evt.ServerId, evt.SenderName,
                        evt.Message, evt.FromActivePlayer), cancellationToken).ConfigureAwait(false);
```

and the clan equivalent with `ChatChannelKind.Clan`. `StopAsync` cancels then awaits **both** loops. Each loop keeps the mandated shape: `OperationCanceledException` swallowed, broad catch with the CA1031 pragma and its own `[LoggerMessage]` partial.

Also record the clan sender's display name for later roster rendering. `IClanStore` is scoped, so resolve it from a fresh scope inside the clan loop:

```csharp
                // Clan members arrive as Steam ids only; chat is where we learn their names.
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var store = scope.ServiceProvider.GetRequiredService<IClanStore>();
                    await store.RecordNameAsync(evt.GuildId, evt.ServerId, evt.SenderSteamId, evt.SenderName,
                        cancellationToken).ConfigureAwait(false);
                }
```

This makes `Features.Chat` reference `RustPlusBot.Persistence` — it already does, for `IMuteStore`.

- [ ] **Step 7: Update registrations**

`ChatServiceCollectionExtensions` — rename the registered types (`IChatWebhookPoster`/`DiscordChatWebhookPoster`, `ChatRelay`, `ChatInboundProcessor`). `RelayDedupBuffer` stays `AddSingleton` here; `AddClans()` will `TryAddSingleton` it.

Update `ChatRegistrationTests` for the renamed types, and add an assertion that both locator kinds resolve into the relay.

- [ ] **Step 8: Run the suites**

Run: `dtk dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: `0 Error(s)`.

Run: `dtk dotnet test RustPlusBot.slnx -maxcpucount:1`
Expected: every assembly passes. Read per-assembly counts — `Features.Chat.Tests` should be substantially higher (each converted test now runs twice, once per kind).

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Features.Chat tests/RustPlusBot.Features.Chat.Tests
git commit -m "$(cat <<'EOF'
Drive the chat bridge by channel kind to serve clan chat

Generalise the relay, webhook poster and inbound processor over
ChatChannelKind so one bridge serves both team and clan chat, and subscribe the
hosted service to both message events. The inbound path picks its kind from
whichever locator claims the channel. Clan chat senders are recorded as the
name source for the clan roster.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```
### Task 8: The clan snapshot differ

A pure function, so this is the highest-value test target in the feature. No I/O, no DI.

**Files:**
- Create: `src/RustPlusBot.Features.Clans/State/ClanChange.cs`
- Create: `src/RustPlusBot.Features.Clans/State/ClanSnapshotDiffer.cs`
- Create: `tests/RustPlusBot.Features.Clans.Tests/State/ClanSnapshotDifferTests.cs`

**Interfaces:**
- Consumes: `ClanSnapshot` and friends (T1).
- Produces:

```csharp
internal enum ClanChangeKind
{
    Dissolved, Renamed, MotdChanged, MemberJoined, MemberLeft,
    MemberPromoted, MemberDemoted, InviteSent, InviteAccepted, InviteRevoked,
    LogoChanged, ColorChanged, ScoreChanged,
}

internal sealed record ClanChange(
    ClanChangeKind Kind,
    ulong? SteamId,
    ulong? ActorSteamId,
    string? Text,
    string? RoleName,
    long? Score);

internal static class ClanSnapshotDiffer
{
    public static IReadOnlyList<ClanChange> Diff(ClanSnapshot? previous, ClanSnapshot? current);
}
```

**Behaviour contract, fixed and deterministic:**

- `previous is null && current is not null` — a **first** snapshot. Emit **nothing**. A newly detected clan must not spray one "joined" per existing member into a brand-new channel.
- `previous is not null && current is null` — emit exactly `[Dissolved]`.
- both null — empty.
- `previous.ClanId != current.ClanId` — the player joined a *different* clan. Treat as a first snapshot: emit nothing.
- Otherwise emit changes in this exact order: `Renamed`, `MotdChanged`, `MemberJoined`, `MemberLeft`, `MemberPromoted`/`MemberDemoted`, `InviteSent`, `InviteAccepted`, `InviteRevoked`, `LogoChanged`, `ColorChanged`, `ScoreChanged`.
- `InviteAccepted` wins over `MemberJoined` + `InviteRevoked` when a steam id leaves `Invites` and appears in `Members` in the same diff. That id must NOT also produce a `MemberJoined`.
- Promotion vs demotion is decided by comparing the `Rank` of the old and new roles, resolved from `current.Roles`. Lower rank = higher standing, so `newRank < oldRank` is a promotion. If either role id is unknown in `current.Roles`, emit neither — an unresolvable role change is not worth a wrong claim.
- String comparisons are `StringComparison.Ordinal`.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Clans.Tests/State/ClanSnapshotDifferTests.cs`. Build snapshots with a private static helper so each test states only what it varies:

```csharp
private static ClanSnapshot Clan(
    long clanId = 1,
    string name = "Wolves",
    string? motd = null,
    ulong? motdAuthor = null,
    string? logoHash = null,
    int? color = null,
    long? score = null,
    IReadOnlyList<ClanRoleSnapshot>? roles = null,
    IReadOnlyList<ClanMemberSnapshot>? members = null,
    IReadOnlyList<ClanInviteSnapshot>? invites = null) => ...;

private static ClanRoleSnapshot Role(int roleId, int rank, string name) =>
    new(roleId, rank, name, false, false, false, false, false, false, false, false, false);

private static ClanMemberSnapshot Member(ulong steamId, int roleId = 1, bool online = false) =>
    new(steamId, roleId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, online);

private static ClanInviteSnapshot Invite(ulong steamId, ulong recruiter = 99) =>
    new(steamId, recruiter, DateTimeOffset.UnixEpoch);
```

Tests:

```csharp
[Fact] public void Emits_nothing_for_a_first_snapshot()
[Fact] public void Emits_nothing_when_both_snapshots_are_null()
[Fact] public void Emits_nothing_when_nothing_changed()
[Fact] public void Emits_dissolved_when_the_clan_goes_away()
[Fact] public void Emits_nothing_when_the_player_joined_a_different_clan()
// previous.ClanId = 1, current.ClanId = 2 with entirely different members => empty.
[Fact] public void Detects_a_rename()
[Fact] public void Detects_a_motd_change_and_carries_the_author()
[Fact] public void Detects_a_member_joining()
[Fact] public void Detects_a_member_leaving()
[Fact] public void Detects_a_promotion_using_rank_order()
// Roles: 1 => rank 2 "Member", 2 => rank 0 "Leader". Member moves 1 -> 2 => MemberPromoted, RoleName "Leader".
[Fact] public void Detects_a_demotion_using_rank_order()
[Fact] public void Emits_no_role_change_when_the_new_role_is_unknown()
[Fact] public void Detects_an_invite_being_sent()
[Fact] public void Detects_an_invite_being_revoked()
[Fact] public void Reports_an_accepted_invite_once_and_not_as_a_join()
// previous: invites [7], members []. current: invites [], members [7].
// Assert exactly one change, Kind == InviteAccepted, SteamId == 7.
[Fact] public void Detects_a_logo_change()
[Fact] public void Detects_a_colour_change()
[Fact] public void Detects_a_score_change_and_carries_the_new_score()
[Fact] public void Orders_changes_deterministically()
// A snapshot pair that changes name, motd, members, invites and score at once;
// assert the Kind sequence equals the documented order exactly.
```

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/RustPlusBot.Features.Clans.Tests -maxcpucount:1 --filter FullyQualifiedName~ClanSnapshotDifferTests`
Expected: FAIL — `ClanSnapshotDiffer` does not exist.

- [ ] **Step 3: Implement `ClanChange`**

`src/RustPlusBot.Features.Clans/State/ClanChange.cs`:

```csharp
namespace RustPlusBot.Features.Clans.State;

/// <summary>The kind of clan change detected between two snapshots.</summary>
internal enum ClanChangeKind
{
    /// <summary>The clan was dissolved, or the player left it.</summary>
    Dissolved = 0,

    /// <summary>The clan was renamed.</summary>
    Renamed = 1,

    /// <summary>The message of the day changed.</summary>
    MotdChanged = 2,

    /// <summary>A member joined the clan.</summary>
    MemberJoined = 3,

    /// <summary>A member is no longer in the clan. The API cannot distinguish leaving from being kicked.</summary>
    MemberLeft = 4,

    /// <summary>A member moved to a higher-standing role.</summary>
    MemberPromoted = 5,

    /// <summary>A member moved to a lower-standing role.</summary>
    MemberDemoted = 6,

    /// <summary>A player was invited to the clan.</summary>
    InviteSent = 7,

    /// <summary>An invited player joined, observed as a single invite-to-member transition.</summary>
    InviteAccepted = 8,

    /// <summary>An invitation went away without the player joining.</summary>
    InviteRevoked = 9,

    /// <summary>The clan logo changed.</summary>
    LogoChanged = 10,

    /// <summary>The clan colour changed.</summary>
    ColorChanged = 11,

    /// <summary>The clan score changed.</summary>
    ScoreChanged = 12,
}

/// <summary>One detected clan change, ready to be rendered into the #claninfo feed.</summary>
/// <param name="Kind">What changed.</param>
/// <param name="SteamId">The player the change is about, or null for clan-wide changes.</param>
/// <param name="ActorSteamId">The player who caused the change (MOTD author, recruiter), or null.</param>
/// <param name="Text">Free text carried by the change (the new name or the new MOTD), or null.</param>
/// <param name="RoleName">The new role name for a promotion or demotion, or null.</param>
/// <param name="Score">The new score for a score change, or null.</param>
internal sealed record ClanChange(
    ClanChangeKind Kind,
    ulong? SteamId = null,
    ulong? ActorSteamId = null,
    string? Text = null,
    string? RoleName = null,
    long? Score = null);
```

- [ ] **Step 4: Implement the differ**

`src/RustPlusBot.Features.Clans/State/ClanSnapshotDiffer.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Clans.State;

/// <summary>
/// Compares two clan snapshots and produces the feed events between them. Pure: no I/O, no clock,
/// no state. Emission order is fixed so the feed reads consistently.
/// </summary>
internal static class ClanSnapshotDiffer
{
    /// <summary>Diffs two snapshots.</summary>
    /// <param name="previous">The last known snapshot, or null when none was stored.</param>
    /// <param name="current">The new snapshot, or null when the clan is gone.</param>
    /// <returns>The detected changes, in a fixed order.</returns>
    public static IReadOnlyList<ClanChange> Diff(ClanSnapshot? previous, ClanSnapshot? current)
    {
        if (previous is null)
        {
            // A first snapshot is a baseline, not news: announcing every existing member as a
            // "joined" would spam a brand-new channel on first detection.
            return [];
        }

        if (current is null)
        {
            return [new ClanChange(ClanChangeKind.Dissolved, Text: previous.Name)];
        }

        if (previous.ClanId != current.ClanId)
        {
            // A different clan entirely; re-baseline rather than diffing unrelated rosters.
            return [];
        }

        var changes = new List<ClanChange>();

        if (!string.Equals(previous.Name, current.Name, StringComparison.Ordinal))
        {
            changes.Add(new ClanChange(ClanChangeKind.Renamed, Text: current.Name));
        }

        if (!string.Equals(previous.Motd, current.Motd, StringComparison.Ordinal))
        {
            changes.Add(new ClanChange(ClanChangeKind.MotdChanged, ActorSteamId: current.MotdAuthor,
                Text: current.Motd));
        }

        var previousMembers = previous.Members.ToDictionary(m => m.SteamId);
        var currentMembers = current.Members.ToDictionary(m => m.SteamId);
        var previousInvites = previous.Invites.Select(i => i.SteamId).ToHashSet();
        var currentInvites = current.Invites.Select(i => i.SteamId).ToHashSet();

        // An id that left Invites and appeared in Members in one step is an acceptance, reported
        // once — never as a join plus a revocation.
        var accepted = previousInvites
            .Where(id => !currentInvites.Contains(id) && currentMembers.ContainsKey(id) && !previousMembers.ContainsKey(id))
            .ToHashSet();

        foreach (var id in currentMembers.Keys.Where(id => !previousMembers.ContainsKey(id) && !accepted.Contains(id))
                     .Order())
        {
            changes.Add(new ClanChange(ClanChangeKind.MemberJoined, id));
        }

        foreach (var id in previousMembers.Keys.Where(id => !currentMembers.ContainsKey(id)).Order())
        {
            changes.Add(new ClanChange(ClanChangeKind.MemberLeft, id));
        }

        var rolesById = current.Roles.ToDictionary(r => r.RoleId);
        foreach (var (id, member) in currentMembers.OrderBy(kv => kv.Key))
        {
            if (!previousMembers.TryGetValue(id, out var before) || before.RoleId == member.RoleId)
            {
                continue;
            }

            if (!rolesById.TryGetValue(member.RoleId, out var newRole) ||
                !rolesById.TryGetValue(before.RoleId, out var oldRole))
            {
                // An unresolvable role change is not worth guessing a direction over.
                continue;
            }

            // Lower Rank is higher standing.
            var kind = newRole.Rank < oldRole.Rank ? ClanChangeKind.MemberPromoted : ClanChangeKind.MemberDemoted;
            changes.Add(new ClanChange(kind, id, RoleName: newRole.Name));
        }

        foreach (var invite in current.Invites.Where(i => !previousInvites.Contains(i.SteamId))
                     .OrderBy(i => i.SteamId))
        {
            changes.Add(new ClanChange(ClanChangeKind.InviteSent, invite.SteamId, invite.Recruiter));
        }

        foreach (var id in accepted.Order())
        {
            changes.Add(new ClanChange(ClanChangeKind.InviteAccepted, id));
        }

        foreach (var id in previousInvites
                     .Where(id => !currentInvites.Contains(id) && !accepted.Contains(id))
                     .Order())
        {
            changes.Add(new ClanChange(ClanChangeKind.InviteRevoked, id));
        }

        if (!string.Equals(previous.LogoHash, current.LogoHash, StringComparison.Ordinal))
        {
            changes.Add(new ClanChange(ClanChangeKind.LogoChanged));
        }

        if (previous.Color != current.Color)
        {
            changes.Add(new ClanChange(ClanChangeKind.ColorChanged));
        }

        if (previous.Score != current.Score)
        {
            changes.Add(new ClanChange(ClanChangeKind.ScoreChanged, Score: current.Score));
        }

        return changes;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Clans.Tests -maxcpucount:1`
Expected: PASS, 36 tests total (17 from Task 7 + 19 here).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Clans/State tests/RustPlusBot.Features.Clans.Tests/State
git commit -m "$(cat <<'EOF'
Add the clan snapshot differ

Compare consecutive clan snapshots into an ordered, deterministic change list.
A first snapshot and a switch to a different clan both re-baseline silently
rather than announcing every existing member, and an invite-to-member
transition is reported once as an acceptance.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Name resolution and the three clan embeds

**Files:**
- Create: `src/RustPlusBot.Features.Clans/Names/IClanNameResolver.cs`
- Create: `src/RustPlusBot.Features.Clans/Names/ClanNameResolver.cs`
- Create: `src/RustPlusBot.Features.Clans/Messages/ClanOverviewMessageRenderer.cs`
- Create: `src/RustPlusBot.Features.Clans/Messages/ClanRosterMessageRenderer.cs`
- Create: `src/RustPlusBot.Features.Clans/Messages/ClanInvitesMessageRenderer.cs`
- Create: `src/RustPlusBot.Features.Clans/ClanComponentIds.cs`
- Create: `tests/RustPlusBot.Features.Clans.Tests/Names/ClanNameResolverTests.cs`
- Create: `tests/RustPlusBot.Features.Clans.Tests/Messages/ClanRendererTests.cs`

**Interfaces:**
- Consumes: `IClanStore` (T4), `ClanSnapshot` (T1), `IMessageRenderer`/`MessageRenderContext`/`MessagePayload` (existing), `ILocalizer` (existing).
- Produces:
  - `internal interface IClanNameResolver { Task<IReadOnlyDictionary<ulong, string>> ResolveAsync(ulong guildId, Guid serverId, IReadOnlyCollection<ulong> steamIds, CancellationToken cancellationToken); }`
  - `internal static class ClanComponentIds` with `public const string SetMotdButtonPrefix = "clan:motd:"` and `public const string SetMotdModalPrefix = "clan:motdmodal:"` and `public const string MotdInputId = "clan:motd:text"`.
  - Three `public sealed class Clan*MessageRenderer : IMessageRenderer`, each with a `public const string Key` matching its `WorkspaceMessageKeys` value.

Renderers are `public` because `IMessageRenderer` is public and they are resolved by the Workspace reconciler across an assembly boundary — the same reason `ServerTeamMessageRenderer` is public.

**Load-bearing requirement — all three renderers.** Each renderer MUST return an empty
`MessagePayload(null, null, null)` when the server has no stored clan. This is not cosmetic:
`ServerInfoRefresher` now has the three clan keys in its `Keys` list, and its per-key flow is
"render → skip if empty → look up the `ProvisionedMessage` → **if missing, full-reconcile and
return**". A clanless server has no clan channel and therefore no clan `ProvisionedMessage`, so a
renderer that returns anything non-empty would trigger a full `ReconcileServerAsync` on *every*
refresh tick, forever, and would also stop the loop before it reached the later keys. The empty
payload is what makes the clan keys inert on clanless servers.

**Name resolution contract:**
`ResolveAsync` returns a dictionary covering **every** requested id. Known ids map to the cached display name; unknown ids map to a Steam profile markdown link, `[{id}](https://steamcommunity.com/profiles/{id})`. Callers never have to null-check.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Clans.Tests/Names/ClanNameResolverTests.cs`:

```csharp
[Fact] public async Task Returns_cached_names_for_known_ids()
[Fact] public async Task Falls_back_to_a_steam_profile_link_for_unknown_ids()
// Assert the value is exactly "[76561198000000000](https://steamcommunity.com/profiles/76561198000000000)".
[Fact] public async Task Covers_every_requested_id()
[Fact] public async Task Returns_an_empty_map_for_an_empty_request_without_querying_the_store()
```

`tests/RustPlusBot.Features.Clans.Tests/Messages/ClanRendererTests.cs`:

```csharp
// Overview
[Fact] public async Task Overview_returns_an_empty_payload_without_a_server_id()
[Fact] public async Task Overview_returns_an_empty_payload_when_no_clan_is_stored()
[Fact] public async Task Overview_shows_the_clan_name_score_and_member_count()
[Fact] public async Task Overview_shows_the_motd_and_its_author()
[Fact] public async Task Overview_uses_the_clan_colour_when_set()
[Fact] public async Task Overview_shows_the_set_motd_button_when_the_active_player_may_set_it()
[Fact] public async Task Overview_hides_the_set_motd_button_when_the_active_player_may_not()
[Fact] public async Task Overview_hides_the_set_motd_button_when_the_active_player_is_not_a_member()

// Roster
[Fact] public async Task Roster_groups_members_by_role_in_rank_order()
// Roles: rank 0 "Leader", rank 1 "Officer", rank 2 "Member".
// Assert the Leader group's text appears before the Officer group's, which precedes Member's.
[Fact] public async Task Roster_puts_online_members_first_within_a_role()
[Fact] public async Task Roster_marks_online_and_offline_members_distinctly()
[Fact] public async Task Roster_shows_notes_when_present()
[Fact] public async Task Roster_falls_back_to_a_profile_link_for_an_unknown_name()
[Fact] public async Task Roster_lists_members_whose_role_is_unknown_under_a_fallback_group()

// Invites
[Fact] public async Task Invites_returns_an_empty_payload_when_there_are_none()
// Assert Text, Embed and Components are ALL null, so the reconciler skips posting.
[Fact] public async Task Invites_lists_the_invitee_and_the_recruiter()
```

Use a substituted `ILocalizer` that returns the key itself (`localizer.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(0))` plus the params overload), so assertions match on keys rather than on English copy. That keeps these tests from breaking when wording changes.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Clans.Tests -maxcpucount:1 --filter FullyQualifiedName~Clan`
Expected: FAIL — the renderer types do not exist.

- [ ] **Step 3: Implement the name resolver**

`Names/IClanNameResolver.cs` — the interface above with full `///` docs.

`Names/ClanNameResolver.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Names;

/// <summary>
/// Resolves clan member Steam ids to display names. The clan API reports members by id only, so
/// names are harvested from clan chat and team snapshots; an id we have never seen a name for
/// renders as a Steam profile link rather than a bare number.
/// </summary>
/// <param name="store">Supplies the cached names.</param>
internal sealed class ClanNameResolver(IClanStore store) : IClanNameResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ulong, string>> ResolveAsync(ulong guildId,
        Guid serverId,
        IReadOnlyCollection<ulong> steamIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(steamIds);
        if (steamIds.Count == 0)
        {
            return new Dictionary<ulong, string>();
        }

        var known = await store.GetNamesAsync(guildId, serverId, steamIds, cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<ulong, string>(steamIds.Count);
        foreach (var id in steamIds)
        {
            result[id] = known.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : ProfileLink(id);
        }

        return result;
    }

    private static string ProfileLink(ulong steamId)
    {
        var id = steamId.ToString(CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"[{id}](https://steamcommunity.com/profiles/{id})");
    }
}
```

- [ ] **Step 4: Implement the component ids**

`src/RustPlusBot.Features.Clans/ClanComponentIds.cs`:

```csharp
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
```

- [ ] **Step 5: Implement the overview renderer**

`Messages/ClanOverviewMessageRenderer.cs`. Shape it on `ServerInfoMessageRenderer`:

- Guard: `if (context.ServerId is not Guid serverId) return new MessagePayload(null, null, null);`
- Load `await store.GetAsync(context.GuildId, serverId, ct)`. When null, return an empty payload — the channel only exists while a clan does, so "no clan" here means the teardown reconcile has not run yet, and leaving the old embed briefly is better than posting a contradiction.
- Title: `clan.overview.title` formatted with the clan name.
- Colour: `clan.Color is { } argb ? new Color((uint)argb & 0x00FFFFFFu) : Color.DarkGrey`. Mask off the alpha byte — Discord embed colours are 24-bit and a packed ARGB value passed raw renders as the wrong colour.
- Fields (all labels via `localizer.Get`, all numbers via `ToString(CultureInfo.InvariantCulture)`):
  - `clan.overview.score` — `Score?.ToString(...)` or `clan.overview.unknown`
  - `clan.overview.members` — `"{count}/{max}"`, or just the count when `MaxMemberCount` is null
  - `clan.overview.created` — Discord relative timestamp: `TimestampTag.FromDateTimeOffset(clan.Created, TimestampTagStyles.Relative)`
  - `clan.overview.leader` — the member holding the lowest-`Rank` role, resolved through `IClanNameResolver`. When the creator is a different id, add a `clan.overview.creator` field too.
  - `clan.overview.motd` — the MOTD, plus author name and a relative timestamp, or `clan.overview.nomotd`
- Components: a `ButtonStyle.Primary` button labelled `clan.overview.setmotd` with id `$"{ClanComponentIds.SetMotdButtonPrefix}{serverId}"`, included **only** when the active player is a clan member whose role has `CanSetMotd`. Resolve the active player's Steam id through the existing credential/connection store the way other per-server surfaces do; if that lookup fails, omit the button — never show an action that will be rejected.

- [ ] **Step 6: Implement the roster renderer**

`Messages/ClanRosterMessageRenderer.cs`:

- Same guards as the overview.
- Resolve every member's name in ONE `IClanNameResolver.ResolveAsync` call — not one call per member.
- Group members by `RoleId`, order groups by the role's `Rank` ascending, and order within a group by `Online` descending then `Joined` ascending.
- Members whose `RoleId` is absent from `Roles` go in a final group titled `clan.roster.unknownrole`.
- Each group is an `AddField(roleHeader, body)` where `roleHeader` is the role name plus a permission legend built from the role's flags (only the flags that are true, joined with " · ", each label localized as `clan.roster.perm.<flag>`), and `body` is one line per member:
  - online: `🟢 {name}` plus `clan.roster.joined` with a relative timestamp
  - offline: `⚫ {name}` plus `clan.roster.lastseen` with a relative timestamp
  - append `clan.roster.notes` with the note text when `Notes` is non-blank
- Rust caps clan size, but a Discord embed field value caps at 1024 characters. If a group's body would exceed that, truncate at a line boundary and append `clan.roster.truncated` with the count of omitted members — silently dropping members would misrepresent the roster.

- [ ] **Step 7: Implement the invites renderer**

`Messages/ClanInvitesMessageRenderer.cs`:

- Same guards.
- `if (clan.Invites.Count == 0) return new MessagePayload(null, null, null);` — an empty payload means the reconciler never posts the message at all.
- One resolver call covering both invitee and recruiter ids.
- Description: one line per invite via `clan.invites.line` with the invitee name, the recruiter name, and a relative timestamp.

- [ ] **Step 8: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Clans.Tests -maxcpucount:1`
Expected: PASS, 56 total (36 + 4 resolver + 16 renderer).

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Features.Clans tests/RustPlusBot.Features.Clans.Tests
git commit -m "$(cat <<'EOF'
Render the clan overview, roster and invites embeds

Add the three anchored #claninfo embeds and the name resolver behind them,
which falls back to a Steam profile link for ids we have never seen a name
for. The invites embed renders empty when there are none so the reconciler
skips posting it.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: The Set MOTD button and modal

**Files:**
- Create: `src/RustPlusBot.Features.Clans/Modules/ClanMotdModal.cs`
- Create: `src/RustPlusBot.Features.Clans/Modules/ClanMotdModule.cs`
- Create: `src/RustPlusBot.Features.Clans/Writing/IClanMotdWriter.cs`
- Create: `src/RustPlusBot.Features.Clans/Writing/ClanMotdWriter.cs`
- Create: `tests/RustPlusBot.Features.Clans.Tests/Writing/ClanMotdWriterTests.cs`

**Interfaces:**
- Consumes: `ClanComponentIds` (T9), `IClanStore` (T4), `IRustServerConnection.SetClanMotdAsync` (T2).
- Produces: `internal interface IClanMotdWriter { Task<ClanMotdWriteResult> SetAsync(ulong guildId, Guid serverId, ulong actorSteamId, string motd, CancellationToken cancellationToken); }` and `internal enum ClanMotdWriteResult { Ok, NotPermitted, Failed }`.

Interaction modules are Discord.Net-bound and effectively untestable in this codebase (no other feature unit-tests one), so all decision logic lives in `ClanMotdWriter`, which IS tested. The module only marshals the interaction.

The supervisor already owns the live sockets, so `ClanMotdWriter` needs a way to reach `SetClanMotdAsync`. Extend the existing `IRustServerQuery` seam in `Abstractions` with:

```csharp
    /// <summary>Sets the clan message of the day on a live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="motd">The new message of the day.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True on success; false when not connected or the write failed.</returns>
    Task<bool> SetClanMotdAsync(ulong guildId, Guid serverId, string motd, CancellationToken cancellationToken);
```

and implement it on the supervisor's `IRustServerQuery` implementation exactly like the sibling write methods (`SetSmartSwitchValueAsync` etc.): look up `_liveSockets`, return false when absent, otherwise delegate with `_options.HeartbeatTimeout`.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Clans.Tests/Writing/ClanMotdWriterTests.cs`:

```csharp
[Fact] public async Task Rejects_a_writer_with_no_stored_clan()
// store.GetAsync => null => NotPermitted.
[Fact] public async Task Rejects_an_actor_who_is_not_a_clan_member()
[Fact] public async Task Rejects_an_actor_whose_role_cannot_set_the_motd()
[Fact] public async Task Rejects_an_actor_whose_role_id_is_unknown()
// A member pointing at a role missing from Roles must not be assumed permitted.
[Fact] public async Task Writes_the_motd_when_the_actor_is_permitted()
// Assert query.SetClanMotdAsync received the exact text and the result is Ok.
[Fact] public async Task Reports_Failed_when_the_socket_write_fails()
[Fact] public async Task Trims_and_rejects_a_blank_motd()
// "   " => Failed, and the socket is never touched.
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Clans.Tests -maxcpucount:1 --filter FullyQualifiedName~ClanMotdWriterTests`
Expected: FAIL — `ClanMotdWriter` does not exist.

- [ ] **Step 3: Implement the writer**

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Writing;

/// <summary>The outcome of a clan MOTD write.</summary>
internal enum ClanMotdWriteResult
{
    /// <summary>The MOTD was set.</summary>
    Ok = 0,

    /// <summary>The acting player's clan role does not allow setting the MOTD.</summary>
    NotPermitted = 1,

    /// <summary>The write did not succeed: no live socket, a rejected write, or blank input.</summary>
    Failed = 2,
}

/// <summary>
/// Applies a clan MOTD change, enforcing the acting player's clan permission before touching the
/// socket. The permission check is duplicated here rather than trusted from the button's presence:
/// component payloads can be forged, and roles can change between render and click.
/// </summary>
/// <param name="store">Supplies the stored clan snapshot for the permission check.</param>
/// <param name="query">Performs the write on the live socket.</param>
internal sealed class ClanMotdWriter(IClanStore store, IRustServerQuery query) : IClanMotdWriter
{
    /// <inheritdoc />
    public async Task<ClanMotdWriteResult> SetAsync(ulong guildId,
        Guid serverId,
        ulong actorSteamId,
        string motd,
        CancellationToken cancellationToken)
    {
        var trimmed = motd?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return ClanMotdWriteResult.Failed;
        }

        var clan = await store.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (clan is null)
        {
            return ClanMotdWriteResult.NotPermitted;
        }

        var member = clan.Members.FirstOrDefault(m => m.SteamId == actorSteamId);
        if (member is null)
        {
            return ClanMotdWriteResult.NotPermitted;
        }

        var role = clan.Roles.FirstOrDefault(r => r.RoleId == member.RoleId);
        if (role is null || !role.CanSetMotd)
        {
            return ClanMotdWriteResult.NotPermitted;
        }

        var ok = await query.SetClanMotdAsync(guildId, serverId, trimmed, cancellationToken).ConfigureAwait(false);
        return ok ? ClanMotdWriteResult.Ok : ClanMotdWriteResult.Failed;
    }
}
```

There is deliberately no `NotConnected` member. `IRustServerQuery.SetClanMotdAsync` collapses "no live socket" into `false`, so such a member would be unreachable, and the user-facing message is the same either way. Do not add one.

- [ ] **Step 4: Implement the modal and module**

`Modules/ClanMotdModal.cs`, modelled on `SwitchRenameModal`:

```csharp
using Discord;
using Discord.Interactions;

namespace RustPlusBot.Features.Clans.Modules;

/// <summary>The modal that collects a new clan MOTD. Handled by <see cref="ClanMotdModule"/>.</summary>
public sealed class ClanMotdModal : IModal
{
    /// <summary>The new message of the day.</summary>
    [InputLabel("Message of the day")]
    [ModalTextInput(ClanComponentIds.MotdInputId, TextInputStyle.Paragraph, maxLength: 1024)]
    public string Motd { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Title => "Set clan MOTD";
}
```

`Modules/ClanMotdModule.cs`, modelled on `SwitchComponentModule`:

- `[ComponentInteraction(ClanComponentIds.SetMotdButtonPrefix + "*")] public async Task OpenAsync(string tail)` — `await RespondWithModalAsync<ClanMotdModal>(ClanComponentIds.SetMotdModalPrefix + tail)`.
- `[ModalInteraction(ClanComponentIds.SetMotdModalPrefix + "*")] public async Task SubmitAsync(string tail, ClanMotdModal modal)` — parse `tail` as a `Guid` with `Guid.TryParse`; on failure respond ephemerally with the localized error and return. Otherwise `DeferAsync(ephemeral: true)`, open a scope from `IServiceScopeFactory`, resolve the acting player's Steam id for `(guild, server)` from the credential store, call `IClanMotdWriter.SetAsync`, then `FollowupAsync` with the localized string for the result, ephemeral. On `Ok`, also resolve `IServerInfoRefresher` and refresh so the overview embed updates immediately rather than at the next tick.
- Both handlers guard `Context.Guild is null` the way `SettingsComponentModule` does.
- No `[RequireUserPermission]`: clan permission is enforced by the writer against the *in-game* role, which is the authority here, not a Discord permission.

Register the assembly for module discovery in Task 11's DI, via `services.AddSingleton(new InteractionModuleAssembly(typeof(ClansServiceCollectionExtensions).Assembly));`.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Clans.Tests -maxcpucount:1`
Expected: PASS, 63 total.

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Abstractions src/RustPlusBot.Features.Connections \
        src/RustPlusBot.Features.Clans tests/RustPlusBot.Features.Clans.Tests
git commit -m "$(cat <<'EOF'
Add the clan MOTD button and modal

Route the Set MOTD interaction through ClanMotdWriter, which re-checks the
acting player's in-game clan permission rather than trusting the button's
presence, then writes through a new IRustServerQuery method.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: Clan state, the change feed, and module wiring

The task that makes everything live: apply snapshots, drive the capability, post the feed, and register it all.

**Files:**
- Create: `src/RustPlusBot.Features.Clans/State/ClanStateService.cs`
- Create: `src/RustPlusBot.Features.Clans/State/ClanCapabilityProvider.cs`
- Create: `src/RustPlusBot.Features.Clans/Posting/IClanFeedPoster.cs`
- Create: `src/RustPlusBot.Features.Clans/Posting/DiscordClanFeedPoster.cs`
- Create: `src/RustPlusBot.Features.Clans/Messages/ClanChangeRenderer.cs`
- Create: `src/RustPlusBot.Features.Clans/Hosting/ClansHostedService.cs`
- Create: `src/RustPlusBot.Features.Clans/ClansServiceCollectionExtensions.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/IClanInfoChannelLocator.cs` and `ClanInfoChannelLocator.cs` (same shape as the clan-chat locator; register it in `WorkspaceServiceCollectionExtensions`)
- Create: `tests/RustPlusBot.Features.Clans.Tests/State/ClanStateServiceTests.cs`
- Create: `tests/RustPlusBot.Features.Clans.Tests/ClansRegistrationTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–10.
- Produces:
  - `internal sealed class ClanStateService` with `Task ApplyAsync(ClanStateChangedEvent evt, CancellationToken ct)`.
  - `internal sealed class ClanCapabilityProvider : IWorkspaceCapabilityProvider` (`Capability => WorkspaceCapabilities.Clan`).
  - `internal interface IClanFeedPoster { Task PostAsync(ulong channelId, string text, CancellationToken ct); }`.
  - `internal sealed class ClanChangeRenderer` with `string? Render(ClanChange change, IReadOnlyDictionary<ulong, string> names, string culture)`.
  - `public static IServiceCollection AddClans(this IServiceCollection services)`.

**`ClanStateService.ApplyAsync` contract** — the heart of the feature:

| `evt.Status` | Behaviour |
| --- | --- |
| `Unavailable` | Return immediately. Change nothing, post nothing, reconcile nothing. |
| `NoClan` | `ClearAsync`. If it returned false (no row existed), return — nothing changed. Otherwise post the `Dissolved` feed line, then `ReconcileServerAsync` so the channels are torn down. Post BEFORE reconciling: the channel is about to be deleted. |
| `HasClan` | Load the previous snapshot, `SaveAsync` the new one, diff, harvest member names, post each rendered change. If the previous snapshot was null (first detection), also `ReconcileServerAsync` so the channels get created. |

Reconcile is called only on a **transition** (none→clan, clan→none), never on every snapshot: `OnClanChanged` fires on any clan edit, and reconciling on each one would hammer Discord.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Clans.Tests/State/ClanStateServiceTests.cs`:

```csharp
[Fact] public async Task Unavailable_changes_nothing()
// store.ClearAsync/SaveAsync never called; reconciler never called; poster never called.
[Fact] public async Task NoClan_clears_the_state_and_reconciles()
[Fact] public async Task NoClan_posts_the_dissolved_line_before_reconciling()
// Use Received.InOrder to assert poster then reconciler.
[Fact] public async Task NoClan_does_nothing_when_there_was_no_stored_clan()
// ClearAsync returns false => no reconcile, no post.
[Fact] public async Task First_detection_saves_and_reconciles_without_posting_changes()
// Previous null => SaveAsync called, ReconcileServerAsync called, poster NOT called.
[Fact] public async Task A_subsequent_snapshot_saves_and_posts_changes_without_reconciling()
// Previous non-null with a different name => SaveAsync called, poster called once,
// ReconcileServerAsync NOT called.
[Fact] public async Task Posts_nothing_when_the_snapshot_is_unchanged()
[Fact] public async Task Does_not_post_when_the_claninfo_channel_is_not_provisioned()
// Locator returns null => poster never called, but the snapshot is still saved.
```

`tests/RustPlusBot.Features.Clans.Tests/ClansRegistrationTests.cs` — modelled on `ChatRegistrationTests.cs`:

```csharp
[Fact] public void Resolves_every_registered_clan_service()
// Real ServiceCollection + the substitutes AddClans needs, then
// BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }) and resolve each.
[Fact] public void Registers_the_clan_capability_provider()
// Resolve IEnumerable<IWorkspaceCapabilityProvider> and assert one has Capability == "clan".
[Fact] public void Registers_the_three_clan_message_renderers()
[Fact] public void Does_not_register_a_second_chat_bridge()
// Features.Chat owns both bridges; assert AddClans registers no ChatRelay/IChatWebhookPoster.
```

- [ ] **Step 2: Run to verify failure**

Run: `dtk dotnet test tests/RustPlusBot.Features.Clans.Tests -maxcpucount:1`
Expected: FAIL — the types do not exist.

- [ ] **Step 3: Implement the change renderer**

`Messages/ClanChangeRenderer.cs` — a small class taking `ILocalizer`, mapping each `ClanChangeKind` to its `clan.event.*` key and arguments:

```csharp
    /// <summary>Renders one change into a feed line, or null when it should not be posted.</summary>
    /// <param name="change">The change to render.</param>
    /// <param name="names">Resolved display names covering every id referenced by the change.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The feed line, or null.</returns>
    public string? Render(ClanChange change, IReadOnlyDictionary<ulong, string> names, string culture)
```

Key mapping, one localized key per kind:

| Kind | Key | Arguments |
| --- | --- | --- |
| `Dissolved` | `clan.event.dissolved` | clan name |
| `Renamed` | `clan.event.renamed` | new name |
| `MotdChanged` | `clan.event.motd` | author name, new MOTD |
| `MemberJoined` | `clan.event.joined` | member name |
| `MemberLeft` | `clan.event.left` | member name |
| `MemberPromoted` | `clan.event.promoted` | member name, role name |
| `MemberDemoted` | `clan.event.demoted` | member name, role name |
| `InviteSent` | `clan.event.invited` | invitee name, recruiter name |
| `InviteAccepted` | `clan.event.inviteaccepted` | member name |
| `InviteRevoked` | `clan.event.inviterevoked` | invitee name |
| `LogoChanged` | `clan.event.logo` | — |
| `ColorChanged` | `clan.event.color` | — |
| `ScoreChanged` | `clan.event.score` | new score, `ToString(CultureInfo.InvariantCulture)` |

Missing names fall back to `names[id]` being absent — use `names.TryGetValue(id, out var n) ? n : id.ToString(CultureInfo.InvariantCulture)` so a rendering never throws. `MotdChanged` with a null `Text` renders `clan.event.motdcleared` instead.

**Score throttling:** the spec calls for at most one score post per refresh interval. Implement it in `ClanStateService`, not here: keep a `ConcurrentDictionary<(ulong, Guid), DateTimeOffset>` of the last score post driven by `IClock`, and drop a `ScoreChanged` change when the last one was under 60 seconds ago. Score moves on every kill; unthrottled it would drown the feed.

- [ ] **Step 4: Implement the poster and locator**

`Posting/DiscordClanFeedPoster.cs` — a thin `DiscordSocketClient` wrapper: resolve the channel, `SendMessageAsync(text, allowedMentions: AllowedMentions.None)`, wrapped in the standard broad catch + `[LoggerMessage]`. Feed lines are plain bot messages, not webhook impersonation — they are the bot speaking, not a player.

`IClanInfoChannelLocator` / `ClanInfoChannelLocator` in Workspace — copy the clan-chat pair, keyed on `WorkspaceChannelKeys.ServerClanInfo`. It needs only `GetChannelIdAsync`; keep `ResolveAsync` off this one, since nothing reads Discord messages from `#claninfo`.

- [ ] **Step 5: Implement the state service**

`State/ClanStateService.cs` — a singleton taking `IServiceScopeFactory`, `IClanInfoChannelLocator`, `IClanFeedPoster`, `ClanChangeRenderer`, `IClock`, and a logger. It resolves `IClanStore`, `IClanNameResolver`, `IWorkspaceStore` (for the culture) and `IWorkspaceReconciler` from a fresh scope per call — never captured. All four are scoped, so capturing any of them on this singleton would be a captive dependency that `ValidateScopes = true` catches. Implement exactly the table above.

Name harvesting: before rendering, collect every `SteamId`/`ActorSteamId` referenced by the diff plus every member in the new snapshot, and resolve them in one `IClanNameResolver` call.

- [ ] **Step 6: Implement the capability provider**

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.State;

/// <summary>
/// Reports the "clan" capability as available exactly while a clan snapshot is stored for the
/// server, which is what gates the two clan channels.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped clan store.</param>
internal sealed class ClanCapabilityProvider(IServiceScopeFactory scopeFactory) : IWorkspaceCapabilityProvider
{
    /// <inheritdoc />
    public string Capability => WorkspaceCapabilities.Clan;

    /// <inheritdoc />
    public async ValueTask<bool> IsAvailableAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken)
    {
        // Clans are per-server; the global scope never has clan channels.
        if (serverId is not Guid id)
        {
            return false;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IClanStore>();
            return await store.HasClanAsync(guildId, id, cancellationToken).ConfigureAwait(false);
        }
    }
}
```

`InternalsVisibleTo("RustPlusBot.Features.Clans")` was already added to the Workspace project in Task 5, so `IWorkspaceCapabilityProvider` is visible here.

**Three constraints carried over from Task 5's review, all load-bearing:**

1. **Never answer `false` on a transient failure.** A `false` answer deletes the Discord channel and all its history. Answering from `IClanStore.HasClanAsync` is safe because `ClanStateService` clears that row only on a *definitive* `NoClan`, never on `Unavailable`. Do **not** add a `try/catch` that swallows a store exception into `false` — the reconciler deliberately lets an exception propagate and fail the reconcile, which is the safe outcome. Failing loudly beats deleting a user's channels.
2. **Register exactly one provider per capability name.** `WorkspaceRegistry` indexes providers with `ToDictionary`, which throws at singleton construction on a duplicate key. Register `ClanCapabilityProvider` once.
3. **Cache within a reconcile pass.** Two gated specs mean two `IsAvailableAsync` calls per server per reconcile, and the heal path reconciles every server on a timer. Opening a DI scope and hitting the DB on each call is wasteful; use a short TTL cache (driven by `IClock`, as `CachingChannelLocator` does). Keep the TTL well under the reconcile interval so a clan transition is still picked up promptly.

- [ ] **Step 7: Implement the hosted service**

`Hosting/ClansHostedService.cs` — a **single** consumer loop. Clan chat is not handled here at all: `Features.Chat` owns both bridges after Task 7, and clan chat senders are already recorded as names there.

- `SubscribeAsync<ClanStateChangedEvent>` → `stateService.ApplyAsync`.

The loop follows the mandated shape verbatim: `Task.Run` in `StartAsync`, `await foreach`, `OperationCanceledException` swallowed, broad catch with the CA1031 pragma and a `[LoggerMessage]` partial. `StopAsync` cancels then awaits the loop task. There is no `MessageReceived` hook — nothing in `#claninfo` is read back.

- [ ] **Step 8: Implement `AddClans()`**

```csharp
public static IServiceCollection AddClans(this IServiceCollection services)
{
    ArgumentNullException.ThrowIfNull(services);

    services.AddRustPlusBotLocalization();

    services.AddSingleton<IClanFeedPoster, DiscordClanFeedPoster>();
    services.AddSingleton<ClanChangeRenderer>();
    services.AddSingleton<ClanStateService>();
    services.AddSingleton<IWorkspaceCapabilityProvider, ClanCapabilityProvider>();

    // Scoped: these reach the DbContext through IClanStore.
    services.AddScoped<IClanNameResolver, ClanNameResolver>();
    services.AddScoped<IClanMotdWriter, ClanMotdWriter>();
    services.AddScoped<IMessageRenderer, ClanOverviewMessageRenderer>();
    services.AddScoped<IMessageRenderer, ClanRosterMessageRenderer>();
    services.AddScoped<IMessageRenderer, ClanInvitesMessageRenderer>();

    services.AddSingleton(new InteractionModuleAssembly(typeof(ClansServiceCollectionExtensions).Assembly));
    services.AddHostedService<ClansHostedService>();

    return services;
}
```

`ClanStateService` and `ClanCapabilityProvider` are singletons that need scoped stores — both resolve them per call from `IServiceScopeFactory`. The registration test's `ValidateScopes = true` is what proves no captive dependency slipped in.

- [ ] **Step 9: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Clans.Tests -maxcpucount:1`
Expected: PASS, 74 total (63 + 8 state + 3 registration).

Run: `dtk dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: `0 Error(s)`.

- [ ] **Step 10: Commit**

```bash
git add src/RustPlusBot.Features.Clans src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Clans.Tests
git commit -m "$(cat <<'EOF'
Apply clan state, post the change feed, and wire the module

Persist each snapshot, drive the workspace clan capability from its presence,
and post diffed changes into #claninfo. Reconcile runs only on a none-to-clan
or clan-to-none transition, and an Unavailable probe changes nothing at all.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 12: Localization

**Files:**
- Modify: `src/RustPlusBot.Localization/Strings.resx`
- Modify: `src/RustPlusBot.Localization/Strings.fr.resx`

**Interfaces:**
- Consumes: every `clan.*` and `channel.clan*` key referenced in Tasks 9–11.
- Produces: nothing code-facing.

- [ ] **Step 1: Collect the exact key list**

```bash
grep -rhoE '"(channel\.clan[a-z.]*|clan\.[a-z.]+|server\.clan[a-z.]*)"' src/RustPlusBot.Features.Clans \
  | tr -d '"' | sort -u
```

Every key that command prints MUST exist in both resx files. Work from that list, not from memory.

- [ ] **Step 2: Add every key to `Strings.resx`**

Follow the file's existing `<data name="..." xml:space="preserve"><value>...</value></data>` formatting exactly. English copy, using `{0}`/`{1}` positional placeholders matching each `localizer.Get(key, culture, arg0, arg1)` call site. Channel names must be lowercase and Discord-safe (no spaces): `clanchat`, `clan-info`.

- [ ] **Step 3: Add every key to `Strings.fr.resx`**

Same key set, French copy, **same placeholder count and order** in every string. A mismatched placeholder count throws a `FormatException` at render time, not at build time — check each pair.

- [ ] **Step 4: Update the hard-coded key count**

`tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs` asserts an exact total:
`Assert.Equal(315, EnglishKeys().Count);`. Update `315` to the new total. Do not delete the
assertion — it is what catches a key added to one file and silently forgotten in the other.

- [ ] **Step 5: Verify parity**

Run: `dotnet test tests/RustPlusBot.Localization.Tests -maxcpucount:1`
Expected: PASS. The parity tests fail loudly on any key present in one file and not the other,
and on a stale total.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Localization tests/RustPlusBot.Localization.Tests
git commit -m "$(cat <<'EOF'
Localize the clan channels, embeds and change feed

Add the English and French strings for the clan channel names, the three
anchored embeds, the MOTD modal, and every clan feed event.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 13: Host wiring and final verification

**Files:**
- Modify: `src/RustPlusBot.Host/Program.cs`
- Modify: `src/RustPlusBot.Host/RustPlusBot.Host.csproj`
- Modify: `README.md`

**Interfaces:**
- Consumes: `AddClans()` (T11).
- Produces: a composed, running bot.

- [ ] **Step 1: Compose the module**

Add a `ProjectReference` to `RustPlusBot.Features.Clans` in `RustPlusBot.Host.csproj`, matching the existing entries' formatting.

In `Program.cs`, add immediately after `builder.Services.AddChat();`:

```csharp
builder.Services.AddClans();
```

Order matters only in that `AddChat()` and `AddClans()` both register `RelayDedupBuffer` — `AddChat` uses `AddSingleton` and `AddClans` uses `TryAddSingleton`, so this order yields one instance. The registration test in Task 11 covers it either way.

No new options section: the clan embeds refresh on the existing `Workspace` interval, so there is nothing to bind or validate.

- [ ] **Step 2: Verify the composed graph starts**

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: `0 Warning(s) 0 Error(s)`.

Run: `dotnet run --project src/RustPlusBot.Host -- --help 2>&1 | head -20` (or start the host with an invalid Discord token and confirm it fails on token validation, not on DI).
Expected: the host reaches Discord login. A `ValidateOnStart` or DI resolution failure here means a captive dependency the registration test did not cover — fix it before continuing.

- [ ] **Step 3: Run the entire suite and read per-assembly counts**

Run: `dotnet test RustPlusBot.slnx -maxcpucount:1`

Expected: every assembly passes. **Read the per-assembly counts.** `RustPlusBot.Features.Clans.Tests` must report **74**, not 0. An assembly reporting 0 means it failed to build and its tests silently did not run.

Record the actual per-assembly numbers in the commit message or PR body — not "all tests pass".

- [ ] **Step 4: Document the feature**

Add a short section to `README.md` alongside the other feature descriptions, matching their tone and length:

- clan channels appear automatically when the paired player is in a clan and are removed when they leave
- `#clanchat` is bidirectional
- `#claninfo` carries pinned overview/roster/invite embeds plus a live change feed
- the Set MOTD button respects the player's in-game clan role
- note the API limits: no clan audit log, no per-member scores, no kick/invite/promote from Discord

- [ ] **Step 5: Run the formatting gate**

This is slow. Run it ONCE, here, and nowhere earlier.

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder"
git diff --stat
```

If the tool changed files, review the diff, then re-run the build and the full test suite before committing. CI fails on any diff this tool would produce.

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
Compose the clan module into the host

Register AddClans, document the feature and its API limits in the README, and
apply the repository formatting profile.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 7: Manual verification checklist**

Automated tests cannot cover the Discord and Rust+ integration surfaces. Against a live server, confirm:

1. A paired player with **no** clan produces **no** `#clanchat` and **no** `#claninfo`.
2. Creating a clan in game makes both channels appear within one reconcile, with the three embeds pinned.
3. A message in game reaches `#clanchat`; a message in `#clanchat` reaches the game exactly once (no double-post, no echo loop).
4. `!`-prefixed lines and `[R+]` lines stay out of `#clanchat`.
5. A member joining, being promoted, and leaving each produce exactly one feed line.
6. Changing the MOTD in game posts a feed line and updates the overview embed.
7. The Set MOTD button appears only for a player whose in-game role permits it, and writing through it changes the MOTD in game.
8. Leaving the clan deletes both channels and leaves no orphaned rows (verify `ClanStates`, `ProvisionedChannel` and `ProvisionedMessage` in the DB).
9. Killing the Rust+ socket mid-session does **not** delete the channels — the Unavailable path preserves state.
10. Switching the guild language to French re-renders both channel names and all embeds.

---

## Self-Review

**Spec coverage.** Every spec section maps to a task: capability survey → the Verified API facts block; scope model → Task 1; detection → Tasks 2–3; conditional channels → Task 5; `#clanchat` → Tasks 6–7; `#claninfo` embeds → Task 9; name resolution → Task 9; refresh → Task 5 Step 11; event feed → Tasks 8 and 11; persistence → Task 4; localization → Task 12; error handling → distributed, with the `Unavailable` rule enforced in Tasks 2, 3 and 11; testing → every task; build constraints → Global Constraints and Task 13.

**Deviation from the spec, resolved in the spec.** The spec originally called for a clan logo thumbnail on the overview embed. `MessagePayload` carries no attachment, so rendering it would mean threading file uploads through `IWorkspaceGateway` and `RenderCanonicalizer` — a large change to shared provisioning code for a decorative image. The spec has been amended to drop the thumbnail while keeping the logo hash, so `LogoChanged` feed events still work.

**Known unreachable code.** `ClanMotdWriteResult.NotConnected` cannot currently be returned, because `IRustServerQuery.SetClanMotdAsync` collapses "no live socket" into `false`. Documented at its definition in Task 10.

**Two additions beyond the spec's file list**, both forced by the code as it actually stands:
- `IWorkspaceStore.DeleteChannelAsync` (Task 5) — the store had only `DeleteScopeAsync`, so removing a single capability-gated channel had no persistence path.
- Moving `RelayDedupBuffer` into `Abstractions` (Task 6) — it was `internal` to `Features.Chat`, and the spec requires one shared instance across both bridges.
