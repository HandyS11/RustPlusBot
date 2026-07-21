# Clan support — design

**Date:** 2026-07-21
**Branch:** `feat/clan-support`
**Status:** approved

## Problem

The Rust "clan" update adds a clan system alongside the existing team system. A paired
player may be in a clan or not, and may join or leave one at any time. The bot currently
has no awareness of clans at all.

We want:

1. Detection of whether the paired player on a given Rust server is in a clan.
2. A `#clanchat` channel bridging clan chat, mirroring the existing `#teamchat` bridge.
3. A `#claninfo` channel presenting clan data — overview, roster, invites — plus a live
   feed of clan changes.
4. Both channels present **only** while a clan exists, and removed when it does not.

## Capability survey — RustPlusApi 2.0.0-beta.4

Verified directly against the shipped assembly and XML docs. Everything the design relies
on already exists; no library change is required.

**Events on `IRustPlus`:**

| Event | Payload | Meaning |
| --- | --- | --- |
| `OnClanChatReceived` | `ClanMessageEventArg(ClanId, ClanMessage)` | new clan chat message |
| `OnClanChanged` | `ClanChangedEventArg(ClanInfo?)` | snapshot replaced; `null` ⇒ clan dissolved or left |

**Requests on `IRustPlus`:**

| Method | Returns |
| --- | --- |
| `GetClanInfoAsync(ct)` | `ClanInfo` — full snapshot |
| `GetClanChatAsync(ct)` | `ClanChatInfo` — recent messages, oldest-first |
| `SendClanMessageAsync(string, ct)` | posts to clan chat |
| `SetClanMotdAsync(string, ct)` | sets the message of the day |

**Error code:** `RustPlusErrorCode.NoClan` (`no_clan`) — the player is not in a clan. This is
the authoritative negative-detection signal.

**Models (`RustPlusApi.Data.Clans`)** — exact CLR types, verified by reflecting over the
shipped assembly:

- `ClanInfo` — `long ClanId`, `string Name`, `DateTime Created`, `ulong Creator`,
  `string? Motd`, `DateTime? MotdTimestamp`, `ulong? MotdAuthor`, `byte[]? Logo`,
  `int? Color` (packed ARGB), `IEnumerable<ClanRole> Roles`,
  `IEnumerable<ClanMember> Members`, `IEnumerable<ClanInvite> Invites`,
  `int? MaxMemberCount`, `long? Score`
- `ClanMember` — `ulong SteamId`, `int RoleId`, `DateTime Joined`, `DateTime LastSeen`,
  `string? Notes`, **`bool? Online`** (nullable — treat `null` as offline)
- `ClanRole` — `int RoleId`, `int Rank` (lower = higher rank), `string Name`, and eight
  `bool` permission flags: `CanSetMotd`, `CanSetLogo`, `CanInvite`, `CanKick`,
  `CanPromote`, `CanDemote`, `CanSetPlayerNotes`, `CanAccessLogs`, `CanAccessScoreEvents`
- `ClanInvite` — `ulong SteamId`, `ulong Recruiter`, `DateTime Timestamp`
- `ClanMessage` — `ulong SteamId`, `string Name`, `string Message`, `DateTime Time`
- `ClanMessageEventArg` is **flat**, not nested: `long ClanId`, `ulong SteamId`,
  `string Name`, `string Message`, `DateTime Time`

All collections are `IEnumerable<T>` and all timestamps are `DateTime`, so the mapping layer
materialises them to `IReadOnlyList<T>` and `DateTimeOffset` (assuming UTC) at the boundary.

Requests return `Response<T>` with `IsSuccess`, `Data`, and `Error?.Code`
(`RustPlusErrorCode`) — the same shape the existing socket source already consumes.

**Deliberately out of scope — no API exists.** `CanAccessLogs` and `CanAccessScoreEvents`
are readable permission flags, but the library exposes no clan audit-log fetch and no
score-event fetch. There is also **no per-member score** — only a single clan-wide
`ClanInfo.Score`. Consequently there is no member-vs-member leaderboard; the roster ranks
on clan role and online status, which is entirely API-derived. Invite / kick / promote /
demote actions are likewise not exposed and are not implemented. Revisit if a later
RustPlusApi release adds them.

## Scope model

A clan belongs to the **paired player on a given Rust server**. Clan state is therefore
scoped `(GuildId, ServerId)`, exactly like team state. A guild with three servers may have
a clan on one and none on the others; the channels are per-server, inside the existing
per-server category.

## Architecture

New feature project `src/RustPlusBot.Features.Clans`, following the established module
convention: one public `AddClans()` extension at project root, everything else
`internal sealed`, registered from `src/RustPlusBot.Host/Program.cs`, with a matching
`tests/RustPlusBot.Features.Clans.Tests`.

### Connection seam

`IRustServerConnection` (`Features.Connections/Listening/`) gains:

```csharp
Task<ClanProbeResult> GetClanInfoAsync(TimeSpan timeout, CancellationToken ct);
Task SendClanMessageAsync(string message, CancellationToken ct);
Task<bool> SetClanMotdAsync(string motd, TimeSpan timeout, CancellationToken ct);

event EventHandler<ClanChatLine>? ClanMessageReceived;
event EventHandler<ClanSnapshot?>? ClanChanged;
```

`GetClanChatAsync` (history) is **not** added: the bridge is live-only, matching teamchat,
and backfilling history on every reconnect would duplicate messages in Discord.

`RustPlusSocketSource` maps `RustPlusApi` types into RustPlusApi-free records, the same way
`OnTeamChatReceived` is mapped to `TeamChatLine` today:

- `ClanChatLine(ulong SteamId, string Name, string Message, DateTimeOffset Time)`
- `ClanSnapshot(...)` and its nested `ClanMemberSnapshot` / `ClanRoleSnapshot` /
  `ClanInviteSnapshot` records, living in `RustPlusBot.Abstractions/Connections/`

`GetClanInfoAsync` distinguishes three outcomes: a snapshot, a definitive `NoClan`, and a
transient failure. It returns `ClanProbeResult(ClanSnapshot? Snapshot, ClanProbeStatus Status)`
with `Status ∈ { HasClan, NoClan, Unavailable }` — collapsing `NoClan` and `Unavailable`
into a single `null` would let a socket hiccup delete a user's channels.

`ConnectionSupervisor`:

- subscribes to both clan events on connect and unsubscribes on disconnect, alongside the
  existing team-chat hookup
- performs one `GetClanInfoAsync` probe on connect, so state is correct after a bot
  restart rather than only after the next in-game change
- publishes `ClanMessageReceivedEvent(GuildId, ServerId, SteamId, Name, Message, Time,
  FromActivePlayer)` and `ClanStateChangedEvent(GuildId, ServerId, ClanSnapshot?,
  ClanProbeStatus)` on `IEventBus`
- implements `IClanChatSender` returning `Sent` / `NotConnected` / `Failed`, mirroring
  `ITeamChatSender`

`RejectedConnection` gets the corresponding no-op implementations.

### Conditional channels — Workspace capability seam

The reconciler currently creates every declared channel unconditionally. Rather than
special-casing clans, add a small generic seam:

```csharp
internal sealed record ChannelSpec(
    WorkspaceScope Scope,
    string Key,
    string NameKey,
    ChannelPermissionProfile Permissions,
    int Order,
    string? Capability = null);

internal interface IWorkspaceCapabilityProvider
{
    string Capability { get; }
    ValueTask<bool> IsAvailableAsync(ulong guildId, Guid? serverId, CancellationToken ct);
}
```

`WorkspaceRegistry` aggregates all registered providers into a keyed lookup. In
`WorkspaceReconciler.EnsureChannelsAsync`:

- spec with `Capability == null` — unchanged behaviour
- spec whose capability resolves **available** — created / adopted as today
- spec whose capability resolves **unavailable** — skipped, and any existing
  `ProvisionedChannel` for that key is deleted through the existing teardown path so the
  `ProvisionedChannel` and dependent `ProvisionedMessage` rows are removed too
- capability with **no registered provider** — treated as unavailable, so a feature that
  isn't composed doesn't leave orphan channels

The existing "retain orphan channels not in the registry" rule is unchanged; capability
removal is an explicit deletion, distinct from a spec disappearing from the registry.

`Features.Clans` registers `ClanCapabilityProvider` with `Capability = "clan"`, backed by
the persisted clan state (row present ⇒ available). When `ClanStateChangedEvent` flips
presence, `ClansHostedService` calls `ReconcileServerAsync`, so channels appear and
disappear within seconds without a restart.

**Teardown is triggered only by a definitive signal** — `OnClanChanged` with a `null`
snapshot, or a `GetClanInfoAsync` probe returning `NoClan`. `Unavailable` leaves the last
known state untouched. Losing the Discord-side `#clanchat` history on clan departure is
accepted; there is no grace period.

### Channel specs

Added to `Specs/ServerWorkspaceSpecProvider.cs`, immediately after teamchat:

| Key | Name key | Profile | Order | Capability |
| --- | --- | --- | --- | --- |
| `clanchat` | `channel.clanchat.name` | Interactive | 2 | `clan` |
| `claninfo` | `channel.claninfo.name` | ReadOnly | 3 | `clan` |

Existing per-server channels shift: events 4, map 5, switches 6, alarms 7,
storagemonitors 8.

## `#clanchat`

A deliberate clone of the teamchat bridge, so it inherits its already-proven edge-case
handling. Files mirror `Features.Chat` one-for-one.

**Game → Discord.** `ClansHostedService` consumes `ClanMessageReceivedEvent` in the
mandated `AlarmsHostedService` loop shape → `ClanChatRelay` → `IClanChatWebhookPoster` /
`DiscordClanChatWebhookPoster` (webhook named `"RustPlusBot ClanChat"`, re-discovered by
name on restart, `ConcurrentDictionary`-cached, `username:` impersonation,
`AllowedMentions.None`).

`ClanChatRelay` drops, in order:

1. lines starting with `BotTeamChat.Prefix` (`[R+]`) from the active player
2. echoes matched in the dedup buffer
3. lines starting with the per-server command prefix from `IMuteStore`

then resolves the channel via `IClanChatChannelLocator` and posts.

**Discord → game.** `ClanChatInboundProcessor` ignores bot and webhook authors and empty
content, reverse-resolves `(guild, server)` from the channel id, applies the mute gate,
formats `"[{DisplayName}] {Content}"` with `CultureInfo.InvariantCulture`, **records in the
dedup buffer before sending** (the in-game echo can beat the send response), then calls
`IClanChatSender.SendAsync`. A `Failed` outcome reacts ❌ on the Discord message.

**Dedup buffer.** `RelayDedupBuffer` is currently keyed `(ulong Guild, Guid Server)`. It
gains a `ChatChannelKind { Team, Clan }` discriminator in the key so a clan echo cannot
cancel an identical team-chat line sent in the same 15 s window. The buffer stays a single
shared singleton; the registration test asserting relay and processor share one instance is
extended to cover the clan pair.

**Locator.** `IClanChatChannelLocator` / `ClanChatChannelLocator` — a subclass of
`CachingChannelLocator` over `WorkspaceChannelKeys.ServerClanChat`, identical in shape to
`TeamChatChannelLocator`, including the reverse `ResolveAsync(channelId)` lookup.

## `#claninfo`

Anchored, self-refreshing embeds **and** the live event feed share this channel. Because
Discord orders messages by creation time, feed messages push the anchored embeds upward, so
the reconciler **pins** each anchored clan message on first post. They remain one click away
in the pin bar while the feed flows below. No delete-and-repost churn.

Pinning is new capability: `MessageSpec` gains a `bool Pinned = false` flag, and
`DiscordWorkspaceGateway` gains `PinMessageAsync(channelId, messageId, ct)`, invoked by
`EnsureMessagesAsync` only when a message is newly posted (pinning is idempotent but costs
an API call, so it is not re-applied on every reconcile). Discord caps a channel at 50 pins;
with three pinned messages that is not a concern. A failed pin is logged and ignored — the
embed is still correct, merely not pinned.

The declaration-order repair logic in `EnsureMessagesAsync` — which deletes and re-posts
later messages when an earlier one must be newly created — remains correct here, and any
re-posted message is re-pinned.

The three renderers live in `Features.Clans/Messages/` (data-owning project renders, per the
`ServerEventsMessageRenderer` precedent), are registered scoped, and are spec'd in
Workspace's `ServerWorkspaceSpecProvider`.

### `clan.overview`

- title: clan name; embed colour unpacked from `ClanInfo.Color` ARGB, falling back to the
  neutral colour when unset
- no logo thumbnail. `ClanInfo.Logo` is raw bytes, and `MessagePayload` carries only
  `(Text, Embed, Components)` — no attachment. Rendering the logo would mean threading file
  uploads through `IWorkspaceGateway.PostMessageAsync`/`EditMessageAsync` and
  `RenderCanonicalizer`, a substantial change to shared provisioning code for a decorative
  thumbnail. The logo is still hashed so the feed can report "logo changed".
- fields: Score · Members (`n/MaxMemberCount`) · Created (Discord relative timestamp) ·
  Leader (holder of the lowest-`Rank` role; `Creator` shown separately when different) ·
  MOTD with author name and relative timestamp
- component: **Set MOTD** button → Discord modal → `SetClanMotdAsync`. Rendered only when
  the paired player's own role has `CanSetMotd`; hidden otherwise. Component ids centralised
  alongside `WorkspaceComponentIds`.

### `clan.roster`

Members grouped by role in ascending `Rank` order (Leader → Officer → Member …), online
members first within each group:

- 🟢 online / ⚫ offline glyph
- display name (see name resolution below)
- `LastSeen` as a relative timestamp for offline members
- `Joined` date
- officer `Notes` when present

Each role group's header carries a compact legend of that role's permission flags.

### `clan.invites`

Pending invites: invitee, recruiter, and when. Returns an empty `MessagePayload` when there
are none, so the reconciler skips posting it entirely.

### Name resolution

`ClanMember` carries only `SteamId` — no display name. A persisted `SteamId → Name` cache
per `(GuildId, ServerId)` is populated from two sources that do carry names: clan chat
message senders, and team snapshots. When a Steam id is unknown, render a
`steamcommunity.com/profiles/{id}` markdown link rather than a bare number — honest about
what we know instead of inventing a placeholder.

### Refresh

The three clan message keys are appended to `ServerInfoRefresher`'s key list, reusing the
existing render → canonicalize → `RenderGate.ShouldSend` → edit → commit path, so unchanged
embeds never produce an API call. Refresh is driven by the existing
`ServerInfoRefreshHostedService` interval; no new option is introduced.

Following the honest-over-stale rule, when the connection is down the overview renders an
explicit "disconnected" embed rather than an empty payload, since an empty payload means
"leave the previous message on screen".

## Event feed

`ClanSnapshotDiffer` is a pure function over `(previous, current)` snapshots, producing a
list of typed clan events. It is driven by `ClanStateChangedEvent` and posted as transient
messages into `#claninfo` by `ClanEventRelay`.

| Event | Trigger |
| --- | --- |
| member joined | new `SteamId` in `Members` |
| member left | `SteamId` gone from `Members` |
| role changed | `RoleId` changed; rendered as promoted or demoted by comparing `Rank` |
| MOTD changed | `Motd` differs; shows new text plus `MotdAuthor` |
| clan renamed | `Name` differs |
| logo changed | logo hash differs |
| colour changed | `Color` differs |
| invite sent | new `SteamId` in `Invites` |
| invite accepted | `SteamId` moves from `Invites` to `Members` |
| invite revoked | `SteamId` leaves `Invites` without joining `Members` |
| score changed | `Score` differs; throttled to at most one post per refresh interval |
| clan dissolved | snapshot becomes `null`; posted before channel teardown |

The API cannot distinguish a voluntary leave from a kick, so the "member left" string is
deliberately neutral ("X is no longer in the clan") rather than guessing. Likewise
"invite accepted" is inferred from the invite-to-member transition within one diff; if the
two changes land in separate snapshots it degrades to "invite revoked" + "member joined",
which is still accurate.

Ordering within one diff is fixed and deterministic: dissolved → renamed → MOTD → members →
roles → invites → cosmetic → score.

## Persistence

One migration, `ClanSupport`.

**`ClanState`** — PK `(GuildId, ServerId)`, `ServerId` FK to `RustServer` with
`OnDelete(DeleteBehavior.Cascade)`.

| Column | Notes |
| --- | --- |
| `ClanId` | clan identifier |
| `Name`, `Created`, `Creator` | identity |
| `Motd`, `MotdAuthor`, `MotdTimestamp` | message of the day |
| `Color`, `LogoHash`, `MaxMemberCount`, `Score` | presentation and stats |
| `MembersJson`, `RolesJson`, `InvitesJson` | serialised collections, for diffing and rendering |
| `LastSeenUtc` | when the snapshot was last confirmed |

Row presence is the single source of truth for `ClanCapabilityProvider`.

Collections are stored as JSON rather than normalised tables: they are only ever read and
written whole (diff, then render), are bounded by `MaxMemberCount`, and nothing queries
across them. A normalised schema would add three tables and a migration burden for no
query benefit.

**`ClanPlayerName`** — PK `(GuildId, ServerId, SteamId)`, columns `Name`, `UpdatedUtc`,
cascade from `RustServer`.

Both are reached through a scoped `IClanStore` in `RustPlusBot.Persistence`. Singletons
never capture it; they resolve it per call from `IServiceScopeFactory.CreateAsyncScope()`,
per the `TeamChatRelay` pattern.

## Localization

Roughly 45 keys, added to **both** `Strings.resx` and `Strings.fr.resx` — the parity test is
a hard CI gate. Namespaces:

- `channel.clanchat.name`, `channel.claninfo.name`
- `server.clan.overview.*` — title, field labels, MOTD, disconnected state
- `server.clan.roster.*` — role legend, online/offline, last-seen, notes
- `server.clan.invites.*`
- `clan.event.*` — one key per feed event type
- `clan.motd.modal.*` — modal title, field label, success and failure responses

## Error handling

- `NoClan` is a **normal state**, never logged as an error and never surfaced to users as a
  failure.
- Any other clan RPC failure yields `ClanProbeStatus.Unavailable`, which preserves the last
  known state. A transient socket error must never delete a user's clan channels.
- Broad `catch (Exception)` only with an inline `#pragma warning disable CA1031` carrying a
  one-line justification, paired with a `[LoggerMessage]`-generated static partial.
- Hosted service loops follow the `AlarmsHostedService` shape verbatim: one `Task.Run` per
  event type, `await foreach` over `IEventBus.SubscribeAsync<T>`, `OperationCanceledException`
  swallowed.
- `SetClanMotdAsync` failures respond ephemerally in the modal rather than silently
  no-opping.

## Testing

New project `tests/RustPlusBot.Features.Clans.Tests`, mirroring the source folder structure.
xUnit `Assert.*` + NSubstitute only, no FluentAssertions, `using Xunit` via global using,
`public sealed class XxxTests`, `[Fact] public async Task Snake_case_names()`, private
static `Build(...)` factory returning the SUT plus its substitutes.

Coverage:

- `ClanChatRelayTests` — drops bot echo, drops dedup echo, drops command prefix, posts
  otherwise
- `ClanChatInboundProcessorTests` — ignores bots and webhooks, mute gate, records dedup
  before send, ❌ on failure
- `RelayDedupBufferTests` — extended: a clan echo does not cancel an identical team line
- `ClanSnapshotDifferTests` — one test per event type, plus first-snapshot (no spurious
  "joined" storm), no-change, and dissolution
- `ClanRosterRendererTests` — role ordering, online-first, unknown-name fallback link
- `ClanRegistrationTests` — real `ServiceCollection`, `AddClans()`, resolve with
  `ValidateScopes = true`
- Workspace: capability-gated channel is created when available, deleted when unavailable,
  and untouched when the capability has no provider
- `Features.Connections`: `GetClanInfoAsync` maps `NoClan` to `ClanProbeStatus.NoClan` and
  other failures to `Unavailable`

## Build and CI constraints

- `-maxcpucount:1` is mandatory on every `dotnet build` and `dotnet test`; read per-assembly
  test counts, never just "passed".
- `TreatWarningsAsErrors` with `AnalysisLevel=latest-all`: XML `///` docs on every
  public/internal type **and member**; `CultureInfo.InvariantCulture` / `StringComparison.Ordinal`
  everywhere; `.ConfigureAwait(false)` on every awaited task in `src/`.
- `dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder"` is a hard gate that
  fails on any diff — run once, right before commit.
- Migration:
  `dotnet ef migrations add ClanSupport --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host`
- No new Discord permissions or intents: the chat bridge already requires Message Content
  Intent and Manage Webhooks.

## Build sequence

1. Abstractions: clan snapshot records, `ClanProbeResult`, `ClanMessageReceivedEvent`,
   `ClanStateChangedEvent`
2. Connections: `IRustServerConnection` members, `RustPlusSocketSource` mapping,
   `RejectedConnection` no-ops, `ConnectionSupervisor` hookup and publishing,
   `IClanChatSender`
3. Domain + Persistence: `ClanState`, `ClanPlayerName`, configurations, `IClanStore`,
   `ClanSupport` migration
4. Workspace: `Capability` on `ChannelSpec`, `IWorkspaceCapabilityProvider`, registry
   aggregation, reconciler create/delete logic, pinning, the two channel specs, the three
   message specs, `IClanChatChannelLocator`
5. Features.Clans: chat bridge (relay, poster, inbound processor, hosted service)
6. Features.Clans: `ClanSnapshotDiffer` and `ClanEventRelay`
7. Features.Clans: the three renderers, MOTD modal, `ClanCapabilityProvider`,
   `ServerInfoRefresher` key extension
8. Localization: all keys in both resx files
9. Host wiring, full test pass, cleanupcode
