# Subsystem 3a — Team chat bridge (design)

**Status:** Approved (brainstorming) · **Created:** 2026-06-15 · **Branch:** `feat/chat-bridge` (off `develop`)
**Predecessor:** 1b-iii removal lifecycle (PR #7, merged `059abec`)
**Catalog rows:** "TeamChat" (B, the backbone) · roadmap row **3** (first slice of three)

---

## 1. Scope

A two-way `#teamchat` ↔ in-game team chat bridge, one channel per registered server. This is the
first slice of subsystem 3; it deliberately ships only the relay so it can de-risk the live-socket
bidirectional traffic and the cross-feature event fan-out **before** the `!command` framework and the
Discord-side surfaces are built on top.

- **Game → Discord:** in-game team chat lines are posted into the server's `#teamchat` channel via a
  **per-player webhook** (webhook message `username` = the sender's Steam name).
- **Discord → game:** any message a member types in `#teamchat` is relayed to in-game team chat as the
  active player, prefixed `[DisplayName] message`.
- **Loop prevention:** **dedup-only** — we drop only the echo of lines the bot itself just sent;
  genuine messages from the bot's active account still bridge.

### Explicitly deferred

- `!command` framework + every `!command` (status/team-intel), **including `!mute`/`!unmute`** → **3b**.
- `#commands` channel, `/help`, `/uptime`, `/leader`, `#activity` log, `#information` team summary → **3c**.
- Per-player Steam **avatars** on the webhook (needs a Steam Web API dependency + SteamId→avatar lookup).
- Team-chat **history persistence** — the bridge is a live relay; no chat is stored.
- Rewriting the user's own typed Discord message through the webhook for visual uniformity — the
  member's message stays as their Discord identity (simpler, standard).

---

## 2. Background (current state, verified)

- **Live socket** is owned by `ConnectionSupervisor` (Connections): a `ConcurrentDictionary<(ulong
  Guild, Guid Server), Handle>`, one socket per `(guild, server)` driven by the **active** credential.
  The socket is created inside `RunAsync`; teardown via `IAsyncDisposable`.
- **Socket seam:** `IRustSocketSource.Create(ip, port, steamId, playerToken)` → `IRustServerConnection`
  (`ConnectAsync`, `GetInfoAsync`, `DisposeAsync`). Real impl `RustPlusSocketSource` wraps RustPlusApi
  `2.0.0-beta.1` and is the project's one untested integration shim.
- **RustPlusApi `2.0.0-beta.1` team-chat surface (verified against the restored package DLL):**
  `RustPlus.SendTeamMessageAsync(...)`; event `OnTeamChatReceived` with a `TeamMessageEventArg`
  carrying `SteamId` / `Name` / `Message` / `Time` (model under `RustPlusApi.Data.Events`);
  `GetTeamChatAsync()` and `GetTeamInfoAsync()` also exist (not needed for 3a). **Exact member names
  are verified during execution**, as with the other socket shims.
- **Event bus** (`IEventBus`, `InMemoryEventBus`, Abstractions): streaming pub/sub; producers
  `PublishAsync<T>`, consumers loop over `SubscribeAsync<T>` in a hosted service. Fans out per
  subscriber (precedent: `WorkspaceHostedService` consumes `ConnectionStatusChangedEvent`).
- **Workspace registry:** `IChannelSpecProvider` / `ServerWorkspaceSpecProvider` contribute per-server
  channels (just `#info` today); `ChannelPermissionProfile.Interactive` already exists and is
  documented "reserved for later chat/command channels"; channels persist as `ProvisionedChannel`
  rows keyed by `ChannelKey`. EN/FR localizer resolves `channel.*.name` keys.
- **Gateway:** `DiscordServiceCollectionExtensions` configures `GatewayIntents.Guilds`,
  `AlwaysDownloadUsers = false`; `DiscordSocketClient` is a singleton.
- **Cross-feature seam precedent:** Connections → Workspace already coupled via the public
  `IServerWorkspaceRemover` seam (1b-iii); features talk over the bus + thin seams.

---

## 3. Architecture (Approach A — new `Features.Chat` project)

```
Connections (owns the socket)                  Features.Chat (NEW — Discord relay)
─────────────────────────────                  ───────────────────────────────────
IRustServerConnection                           TeamChatRelay        (bus subscriber)
  +SendTeamMessageAsync(text)                     • drop dedup echoes
  +team-message received hook                     • post via ITeamChatWebhookPoster
ConnectionSupervisor                            TeamChatInboundListener
  • subscribe socket hook → publish               • DiscordSocketClient.MessageReceived
    TeamMessageReceivedEvent (bus)                • #teamchat msg → dedup-record → ITeamChatSender
  • implement ITeamChatSender (seam)            RelayDedupBuffer  (in-mem, per (guild,server))
                                                ITeamChatWebhookPoster (real = 2nd untested shim)
                                                ChatHostedService (thin: starts loop + listener)
        │  bus + ITeamChatSender                          │  ITeamChatChannelLocator
        └──────────────────────────────┬────────────────┘
                            Workspace (channel + locator)
                            • #teamchat ChannelSpec (Interactive)
                            • ITeamChatChannelLocator (channelId ⇄ (guild,server))
```

Rationale: the socket stays encapsulated in Connections; `Features.Chat` owns all Discord-channel /
webhook concerns; the two communicate over the bus (receive) + a thin send seam — exactly the
established per-feature pattern. (Rejected: building it inside Connections — bloats it and forces a
Connections→Workspace channel dependency; having Chat own the socket — leaks the integration shim.)

---

## 4. Data flow

**Game → Discord**

1. Socket raises a team message → `ConnectionSupervisor` (it knows the active SteamId for the key)
   publishes `TeamMessageReceivedEvent { GuildId, ServerId, SenderSteamId, SenderName, Message,
   FromActivePlayer }`.
2. `TeamChatRelay` consumes. If `FromActivePlayer` **and** `Message` matches a live entry in that
   key's `RelayDedupBuffer` → **consume that one entry and drop** (it's our own echo).
3. Otherwise resolve the channel id via `ITeamChatChannelLocator` → post through
   `ITeamChatWebhookPoster` with `username = SenderName`.

**Discord → game**

1. `DiscordSocketClient.MessageReceived` → `TeamChatInboundListener`. Ignore messages whose author is
   a bot/webhook (`message.Author.IsBot || message.Author.IsWebhook`) — prevents the relay echo loop
   on the Discord side.
2. Ask `ITeamChatChannelLocator`: is this channel a `#teamchat`, and for which `(guild, server)`? If
   not, ignore.
3. Format `text = "[{DisplayName}] {content}"`; **record `text` in the dedup buffer** for that key
   (TTL ~15s) **before** sending; then `ITeamChatSender.SendAsync(guild, server, text)`.
4. Result `NotConnected` / `Failed` → add a ❌ reaction to the member's message. `Sent` → no reaction
   (the member's message stays in place as feedback).

**RelayDedupBuffer** — in-memory, keyed by `(guild, server)`, holds `(text, expiresAt)` entries.
`Record(key, text)` appends with `now + TTL`; `TryConsume(key, text)` removes and returns true for the
first non-expired exact match; both prune expired entries. We dedup on the **full formatted string**
(`[Name] msg`) because that is exactly what the bot player echoes back. Two users sending identical
text create two entries, consumed one-per-echo. Single owner (Features.Chat) sees both the record
(inbound) and consume (the relay) paths.

---

## 5. New / changed types

**Abstractions/Events**

- `TeamMessageReceivedEvent(ulong GuildId, Guid ServerId, ulong SenderSteamId, string SenderName,
  string Message, bool FromActivePlayer)` — record.

**Connections**

- Extend `IRustServerConnection`: `Task SendTeamMessageAsync(string message, CancellationToken)`;
  a received-message hook the supervisor subscribes to (e.g. `event` or an `Action<TeamChatLine>`
  set at creation — implementation detail, decided in the plan).
- `ITeamChatSender` (public seam) `Task<TeamChatSendResult> SendAsync(ulong guildId, Guid serverId,
  string message, CancellationToken)`; enum `TeamChatSendResult { Sent, NotConnected, Failed }`.
  Implemented by `ConnectionSupervisor` (it holds the live connection + active SteamId per key).
- `RustPlusServerConnection` (real shim): wire `OnTeamChatReceived` → invoke the hook (stamping
  `FromActivePlayer = SteamId == activeSteamId`); implement `SendTeamMessageAsync`. Untested shim #1.
  Fake connection in tests can raise team messages on demand.

**Features.Chat (new project, mirrors Pairing/Connections layout)**

- `TeamChatRelay` — bus subscriber; dedup-drop + webhook post.
- `TeamChatInboundListener` — gateway `MessageReceived` handler; testable logic (filter, locate,
  format, dedup-record, send) extracted into a plain method so it is unit-testable; the raw gateway
  wiring stays untested (consistent with other gateway/InteractionModule code here).
- `RelayDedupBuffer` — described in §4.
- `ITeamChatWebhookPoster` + real Discord.Net impl (ensure/cache a webhook per `#teamchat` channel,
  post with `username` override). Untested shim #2.
- `ChatHostedService` — thin: starts the relay subscription loop + attaches the listener.
- `AddChat()` DI extension; registered from the Host.

**Workspace**

- `WorkspaceChannelKeys.ServerTeamChat`; add a `#teamchat` `ChannelSpec` (PerServer, `Interactive`,
  ordered after `#info`) in `ServerWorkspaceSpecProvider`; `channel.teamchat.name` EN/FR entries.
- Public `ITeamChatChannelLocator`: `Task<ulong?> GetChannelIdAsync(guildId, serverId)` (for posting)
  and `Task<(ulong GuildId, Guid ServerId)?> ResolveAsync(ulong channelId)` (for the listener). Backed
  by `ProvisionedChannel` rows where `ChannelKey == teamchat`; **caches** the small channel set in
  memory (the listener fires on every guild message, so avoid a DB hit per message).

---

## 6. Discord gateway + permissions

- `DiscordServiceCollectionExtensions`: intents `Guilds` → `Guilds | GuildMessages | MessageContent`.
  **Message Content is privileged** — document the dev-portal toggle (enable "Message Content Intent"
  on the bot) in [running-locally.md](docs/development/running-locally.md). Acceptable: self-hosted,
  under 100 guilds, no Discord verification required.
- The bot needs **Manage Webhooks** on `#teamchat`. Confirm the provisioning permission set / invite
  scope grants it; the webhook is **lazily ensured + cached** per channel and **re-discovered by name**
  on restart (no new persistence, no new table).

---

## 7. Error handling

- Socket received-message hook, the relay loop, and webhook sends use **broad-catch isolation,
  log-and-continue** — a relay failure must never crash the loop or the supervisor (established
  pattern).
- `SendAsync` with no live socket → `NotConnected` → ❌ reaction; transient send failure → `Failed`
  → ❌ reaction; both logged.
- **Never** put a token/secret in a log or exception message (the 1b-ii rule).

---

## 8. Testing

- **Connections:** fake `IRustServerConnection` raises a team message → supervisor publishes
  `TeamMessageReceivedEvent` (incl. correct `FromActivePlayer`); `ITeamChatSender.SendAsync` routes to
  the live connection and returns `Sent`; returns `NotConnected` when the key has no socket.
- **Features.Chat:** `RelayDedupBuffer` (match → consume-one; expiry; non-match passes; second
  identical echo without a second record passes through); `TeamChatRelay` posts vs. drops via a fake
  `ITeamChatWebhookPoster`; `TeamChatInboundListener` *logic* (bot/webhook filter, locate, format,
  dedup-record, sender call + reaction-on-failure) via the extracted method.
- **Workspace:** `ServerWorkspaceSpecProvider` contributes `#teamchat` PerServer/`Interactive`;
  `ITeamChatChannelLocator` resolves both directions.
- If the locator hits the DB inside a test that runs alongside a background loop, use the **1b-ii
  shared-cache in-memory SQLite** harness pattern (`DataSource=name-{guid};Mode=Memory;Cache=Shared`
  - a kept-open keep-alive connection, each scope opening its own connection).

---

## 9. Process / conventions

- Branch `feat/chat-bridge` off `develop`; subagent-driven per-task TDD + spec/quality reviews, then a
  final whole-feature review.
- Run `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
  (ReSharper) before pushing — the pre-push hook enforces it and it reorders members Roslynator
  doesn't flag.
- Mocking an **internal** interface with NSubstitute needs `<InternalsVisibleTo
  Include="DynamicProxyGenAssembly2" />` in that project's csproj (add to `Features.Chat` if the
  internal seams are mocked).
- New `Features.Chat` project added to `RustPlusBot.slnx` + a `Features.Chat.Tests` project.
- Watch the `Discord` namespace collisions noted in prior subsystems when a file uses both `Discord`
  and a domain type of the same name (alias as needed).

---

## 10. Out of scope

The `!command` framework and all `!commands` (3b); `#commands` / slash commands / `#activity` /
`#information` team summary (3c); Steam avatars; chat persistence; translating user message content
(charter C5 covers bot UI only, not user text). Map / events remain subsystem 2.
