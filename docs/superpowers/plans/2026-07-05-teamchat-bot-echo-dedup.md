# Teamchat Bot-Echo Dedup ([R+] Prefix) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bot-originated in-game team-chat lines (command replies, event/player/alarm notifications) get a `[R+]` prefix and are never re-posted into the Discord #teamchat channel; player speech (in-game and Discord-bridged) is untouched.

**Architecture:** A new `IBotTeamChatSender` decorator in Features.Connections prepends `"[R+] "` and forwards to the existing `ITeamChatSender`. The four bot-originated senders switch to it; `TeamChatRelay` drops any active-player message starting with the prefix. The Discord→game bridge (`TeamChatInboundProcessor` + `RelayDedupBuffer`) is untouched.

**Tech Stack:** .NET / C#, xunit + NSubstitute, EF-free change (no migrations, no .resx).

**Spec:** `docs/superpowers/specs/2026-07-05-teamchat-bot-echo-dedup-design.md`

## Global Constraints

- Repo root: `/home/handys11/Dev/RustPlusBot`. Solution file is `RustPlusBot.slnx` (there is NO `.sln`).
- Work on branch `feat/teamchat-bot-echo-dedup` cut from `develop` in the main checkout (this repo does not use worktrees).
- The prefix literal is exactly `[R+]` (constant `BotTeamChat.Prefix`); sent lines are `"[R+] " + message` (prefix, one space, message). It is NEVER localized — no `.resx` changes anywhere in this plan.
- Every public type/member gets XML doc comments in the repo's existing style (`<summary>`, `<param>`, `<returns>`); implementations of interface methods use `/// <inheritdoc />`.
- Test files do NOT need `using Xunit;` (global using is in place). Tests use NSubstitute substitutes and snake-ish `Verb_noun` fact names, matching neighboring files.
- `docs/superpowers/` is gitignored — never `git add` the spec or this plan.
- Hard CI format gate: `dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder" --no-build --verbosity=ERROR` must leave the tree diff-free (run in final task; requires a prior build).
- **`-maxcpucount:1` is MANDATORY on EVERY `dotnet build` and `dotnet test` invocation** — the ConfigureGitHooks MSBuild target races on `.git/config` in parallel builds, and a broken build silently DROPS that assembly's tests (a low total means a build break, not a pass). Always read per-assembly test counts.
- Build is `-warnaserror`: new public types need XML docs; culture nits (CA1305/CA1307/CA1310) require `CultureInfo.InvariantCulture` / `StringComparison.Ordinal`.
- Do not touch: `TeamChatInboundProcessor`, `RelayDedupBuffer`, `ChatRegistrationTests.cs`, `ChatHostedServiceTests.cs`, `TeamChatInboundProcessorTests.cs` — the bridge path keeps the plain `ITeamChatSender`.

---

### Task 1: `BotTeamChat` prefix + `IBotTeamChatSender` decorator + DI registration

**Files:**
- Create: `src/RustPlusBot.Features.Connections/Listening/BotTeamChat.cs`
- Create: `src/RustPlusBot.Features.Connections/Listening/IBotTeamChatSender.cs`
- Create: `src/RustPlusBot.Features.Connections/Listening/BotTeamChatSender.cs`
- Modify: `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs:25` (add one registration line after the `ITeamChatSender` line)
- Test: Create `tests/RustPlusBot.Features.Connections.Tests/BotTeamChatSenderTests.cs`
- Test: Modify `tests/RustPlusBot.Features.Connections.Tests/ConnectionRegistrationTests.cs` (add resolution assert)

**Interfaces:**
- Consumes: `ITeamChatSender.SendAsync(ulong guildId, Guid serverId, string message, CancellationToken cancellationToken) : Task<TeamChatSendResult>` and enum `TeamChatSendResult { Sent, NotConnected, Failed }` — both already exist in `src/RustPlusBot.Features.Connections/Listening/ITeamChatSender.cs`.
- Produces (used by Tasks 2–6):
  - `public static class BotTeamChat { public const string Prefix = "[R+]"; }` (namespace `RustPlusBot.Features.Connections.Listening`)
  - `public interface IBotTeamChatSender { Task<TeamChatSendResult> SendAsync(ulong guildId, Guid serverId, string message, CancellationToken cancellationToken); }` (same namespace)
  - DI: `IBotTeamChatSender` resolvable as a singleton wherever `AddConnections()` is registered.

- [ ] **Step 1: Create the branch**

```bash
cd /home/handys11/Dev/RustPlusBot
git checkout develop && git pull && git checkout -b feat/teamchat-bot-echo-dedup
```

- [ ] **Step 2: Write the failing tests**

Create `tests/RustPlusBot.Features.Connections.Tests/BotTeamChatSenderTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class BotTeamChatSenderTests
{
    [Fact]
    public async Task Prefixes_the_message_and_forwards()
    {
        var inner = Substitute.For<ITeamChatSender>();
        inner.SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TeamChatSendResult.Sent);
        var serverId = Guid.NewGuid();
        var sut = new BotTeamChatSender(inner);

        var result = await sut.SendAsync(10UL, serverId, "hello", CancellationToken.None);

        Assert.Equal(TeamChatSendResult.Sent, result);
        await inner.Received(1).SendAsync(10UL, serverId, "[R+] hello", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TeamChatSendResult.Sent)]
    [InlineData(TeamChatSendResult.NotConnected)]
    [InlineData(TeamChatSendResult.Failed)]
    public async Task Passes_the_result_through(TeamChatSendResult expected)
    {
        var inner = Substitute.For<ITeamChatSender>();
        inner.SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(expected);
        var sut = new BotTeamChatSender(inner);

        var result = await sut.SendAsync(1UL, Guid.Empty, "x", CancellationToken.None);

        Assert.Equal(expected, result);
    }
}
```

Note: `"[R+] hello"` is intentionally a hardcoded literal (not `BotTeamChat.Prefix + " hello"`) — it pins the exact prefix value.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter "FullyQualifiedName~BotTeamChatSenderTests" -maxcpucount:1`
Expected: build FAILS with CS0246 (`BotTeamChatSender` not found) — that is the red state for brand-new types.

- [ ] **Step 4: Implement the three new files**

Create `src/RustPlusBot.Features.Connections/Listening/BotTeamChat.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>The marker prefix identifying bot-originated in-game team-chat lines.</summary>
public static class BotTeamChat
{
    /// <summary>
    /// Prepended (with a trailing space) to every bot-originated team-chat line so the relay can
    /// drop the echo instead of re-posting it to Discord. Never localized.
    /// </summary>
    public const string Prefix = "[R+]";
}
```

Create `src/RustPlusBot.Features.Connections/Listening/IBotTeamChatSender.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>
/// Relays a bot-originated line (command reply, event/player/alarm notification) into a server's
/// in-game team chat, prefixed with <see cref="BotTeamChat.Prefix"/> so its echo is never re-posted
/// to the Discord #teamchat channel. Player speech bridged from Discord uses
/// <see cref="ITeamChatSender"/> instead.
/// </summary>
public interface IBotTeamChatSender
{
    /// <summary>Sends <paramref name="message"/>, prefixed, to the live socket for (<paramref name="guildId"/>, <paramref name="serverId"/>).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="message">The unprefixed message text.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The send result.</returns>
    Task<TeamChatSendResult> SendAsync(ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken);
}
```

Create `src/RustPlusBot.Features.Connections/Listening/BotTeamChatSender.cs`:

```csharp
using System.Globalization;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Prepends <see cref="BotTeamChat.Prefix"/> and forwards to the raw <see cref="ITeamChatSender"/>.</summary>
/// <param name="inner">The raw team-chat sender.</param>
internal sealed class BotTeamChatSender(ITeamChatSender inner) : IBotTeamChatSender
{
    /// <inheritdoc />
    public Task<TeamChatSendResult> SendAsync(ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken) =>
        inner.SendAsync(guildId,
            serverId,
            string.Create(CultureInfo.InvariantCulture, $"{BotTeamChat.Prefix} {message}"),
            cancellationToken);
}
```

In `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs`, directly after the existing line 25 (`services.AddSingleton<ITeamChatSender>(...)`), add:

```csharp
services.AddSingleton<IBotTeamChatSender, BotTeamChatSender>();
```

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter "FullyQualifiedName~BotTeamChatSenderTests" -maxcpucount:1`
Expected: PASS (4 tests: 1 fact + 3 theory cases).

- [ ] **Step 6: Add the DI resolution assert**

In `tests/RustPlusBot.Features.Connections.Tests/ConnectionRegistrationTests.cs`, add to the usings:

```csharp
using RustPlusBot.Features.Connections.Listening;
```

and inside `Services_Resolve`, directly after `Assert.NotNull(provider.GetRequiredService<IConnectionSupervisor>());`, add:

```csharp
Assert.NotNull(provider.GetRequiredService<IBotTeamChatSender>());
```

- [ ] **Step 7: Run the whole Connections test project**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1`
Expected: PASS, zero failures.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(connections): add IBotTeamChatSender applying the [R+] bot prefix

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `TeamChatRelay` drop rule for prefixed echoes

**Files:**
- Modify: `src/RustPlusBot.Features.Chat/Relaying/TeamChatRelay.cs:20-25`
- Test: Modify `tests/RustPlusBot.Features.Chat.Tests/TeamChatRelayTests.cs`

**Interfaces:**
- Consumes: `BotTeamChat.Prefix` from Task 1 (`RustPlusBot.Features.Connections.Listening`; Features.Chat already references Features.Connections).
- Produces: behavior only — an active-player message starting with `[R+]` is never webhook-posted.

- [ ] **Step 1: Write the failing tests**

Add to `tests/RustPlusBot.Features.Chat.Tests/TeamChatRelayTests.cs` (inside the existing class; no new usings needed):

```csharp
[Fact]
public async Task Drops_bot_originated_echo_by_prefix()
{
    var (relay, poster, _, _) = Build();
    var evt = new TeamMessageReceivedEvent(10UL, Guid.Empty, 555UL, "BotPlayer", "[R+] Cargo Ship entered the map",
        FromActivePlayer: true);

    await relay.RelayAsync(evt, CancellationToken.None);

    await poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(),
        Arg.Any<CancellationToken>());
}

[Fact]
public async Task Posts_teammate_message_even_with_prefix()
{
    var (relay, poster, _, _) = Build();
    var evt = new TeamMessageReceivedEvent(10UL, Guid.Empty, 999UL, "Bob", "[R+] hi", FromActivePlayer: false);

    await relay.RelayAsync(evt, CancellationToken.None);

    await poster.Received(1).PostAsync(777UL, "Bob", "[R+] hi", Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Run them to verify the first fails**

Run: `dotnet test tests/RustPlusBot.Features.Chat.Tests --filter "FullyQualifiedName~TeamChatRelayTests" -maxcpucount:1`
Expected: `Drops_bot_originated_echo_by_prefix` FAILS (poster received a call); `Posts_teammate_message_even_with_prefix` already PASSES (documents the FromActivePlayer gate).

- [ ] **Step 3: Implement the drop rule**

In `src/RustPlusBot.Features.Chat/Relaying/TeamChatRelay.cs`, add to the usings:

```csharp
using RustPlusBot.Features.Connections.Listening;
```

and at the top of `RelayAsync`, BEFORE the existing dedup check, add:

```csharp
if (evt.FromActivePlayer && evt.Message.StartsWith(BotTeamChat.Prefix, StringComparison.Ordinal))
{
    return; // Bot-originated line echoing back; #teamchat carries only human discussion.
}
```

(The existing `dedup.TryConsume` block stays unchanged below it.)

- [ ] **Step 4: Run the whole Chat test project**

Run: `dotnet test tests/RustPlusBot.Features.Chat.Tests -maxcpucount:1`
Expected: PASS, zero failures (bridge dedup tests untouched and green).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Chat tests/RustPlusBot.Features.Chat.Tests
git commit -m "feat(chat): drop [R+]-prefixed active-player echoes from #teamchat

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Switch `CommandDispatcher` to `IBotTeamChatSender`

**Files:**
- Modify: `src/RustPlusBot.Features.Commands/Dispatching/CommandDispatcher.cs:22,37`
- Test: Modify `tests/RustPlusBot.Features.Commands.Tests/Dispatching/CommandDispatcherTests.cs`
- Test: Modify `tests/RustPlusBot.Features.Commands.Tests/Hosting/CommandsHostedServiceTests.cs`
- Test: Modify `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`

**Interfaces:**
- Consumes: `IBotTeamChatSender` from Task 1 (identical `SendAsync` signature; the `using RustPlusBot.Features.Connections.Listening;` is already present in all these files).
- Produces: in-game `!command` replies now reach the game as `[R+] <reply>`; no assertion text changes — the prefix is applied inside `BotTeamChatSender`, below this seam.

This is a mechanical dependency-type swap guarded by the compiler and the existing tests — no new behavior at this seam, so no new test is written.

- [ ] **Step 1: Swap the dependency in the dispatcher**

In `src/RustPlusBot.Features.Commands/Dispatching/CommandDispatcher.cs` change the field (line 22):

```csharp
private readonly IBotTeamChatSender _sender;
```

and the constructor parameter (line 37):

```csharp
IBotTeamChatSender sender,
```

(XML doc for the parameter stays as-is: "Relays the reply into in-game team chat.")

- [ ] **Step 2: Update the three test files mechanically**

In `CommandDispatcherTests.cs`, `CommandsHostedServiceTests.cs`, and `CommandRegistrationTests.cs`, replace every occurrence of the token `ITeamChatSender` with `IBotTeamChatSender`. This covers substitutes (`Substitute.For<IBotTeamChatSender>()`), tuple/record member types, `nameof(IBotTeamChatSender.SendAsync)`, and DI registrations (`services.AddSingleton<IBotTeamChatSender>(Substitute.For<IBotTeamChatSender>());`). No other edits.

- [ ] **Step 3: Run the Commands test project**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests -maxcpucount:1`
Expected: PASS, zero failures.

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Commands tests/RustPlusBot.Features.Commands.Tests
git commit -m "feat(commands): send in-game command replies via IBotTeamChatSender

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Switch `EventRelay` to `IBotTeamChatSender`

**Files:**
- Modify: `src/RustPlusBot.Features.Events/Relaying/EventRelay.cs:17-20` (the `EventRelayChannels` record)
- Test: Modify `tests/RustPlusBot.Features.Events.Tests/Relaying/EventRelayTests.cs`
- Test: Modify `tests/RustPlusBot.Features.Events.Tests/EventRegistrationTests.cs`

**Interfaces:**
- Consumes: `IBotTeamChatSender` from Task 1 (`using RustPlusBot.Features.Connections.Listening;` already present).
- Produces: cargo/heli/chinook and rig lines reach the game as `[R+] <line>`; `EventRelayChannels` is DI-constructed (`AddSingleton<EventRelayChannels>()`), so the property type change re-resolves automatically once Task 1's registration exists.

- [ ] **Step 1: Swap the record property type**

In `src/RustPlusBot.Features.Events/Relaying/EventRelay.cs` change the `EventRelayChannels` record:

```csharp
internal sealed record EventRelayChannels(
    IEventChannelLocator Locator,
    IEventChannelPoster Poster,
    IBotTeamChatSender TeamChatSender);
```

(Property name `TeamChatSender` and its `<param>` doc stay the same; only the type changes.)

- [ ] **Step 2: Update the two test files mechanically**

In `EventRelayTests.cs` and `EventRegistrationTests.cs`, replace every occurrence of the token `ITeamChatSender` with `IBotTeamChatSender` (substitutes, tuple member types, DI registration). No other edits.

- [ ] **Step 3: Run the Events test project**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests -maxcpucount:1`
Expected: PASS, zero failures.

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Events tests/RustPlusBot.Features.Events.Tests
git commit -m "feat(events): broadcast event lines via IBotTeamChatSender

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Switch `PlayerEventRelay` to `IBotTeamChatSender`

**Files:**
- Modify: `src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs:21`
- Test: Modify `tests/RustPlusBot.Features.Players.Tests/PlayerEventRelayTests.cs`
- Test: Modify `tests/RustPlusBot.Features.Players.Tests/PlayerEventRegistrationTests.cs`
- Test: Modify `tests/RustPlusBot.Features.Players.Tests/Hosting/PlayersHostedServiceTests.cs`

**Interfaces:**
- Consumes: `IBotTeamChatSender` from Task 1 (`using RustPlusBot.Features.Connections.Listening;` already present).
- Produces: join/leave/death lines reach the game as `[R+] <line>`.

- [ ] **Step 1: Swap the constructor parameter type**

In `src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs` change the primary-constructor parameter (line 21):

```csharp
IBotTeamChatSender teamChatSender,
```

(Parameter name and `<param>` doc stay the same.)

- [ ] **Step 2: Update the three test files mechanically**

In `PlayerEventRelayTests.cs`, `PlayerEventRegistrationTests.cs`, and `PlayersHostedServiceTests.cs`, replace every occurrence of the token `ITeamChatSender` with `IBotTeamChatSender` (fields, substitutes, tuple member types, `nameof(...)`, DI registration). No other edits.

- [ ] **Step 3: Run the Players test project**

Run: `dotnet test tests/RustPlusBot.Features.Players.Tests -maxcpucount:1`
Expected: PASS, zero failures.

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Players tests/RustPlusBot.Features.Players.Tests
git commit -m "feat(players): broadcast player transition lines via IBotTeamChatSender

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Switch `AlarmStateRelay` to `IBotTeamChatSender`

**Files:**
- Modify: `src/RustPlusBot.Features.Alarms/Relaying/AlarmStateRelay.cs:22-25` (the `AlarmRelayChannels` record)
- Test: Modify `tests/RustPlusBot.Features.Alarms.Tests/AlarmStateRelayTests.cs`
- Test: Modify `tests/RustPlusBot.Features.Alarms.Tests/AlarmRegistrationTests.cs`
- Test: Modify `tests/RustPlusBot.Features.Alarms.Tests/Hosting/AlarmsHostedServiceTests.cs`

**Interfaces:**
- Consumes: `IBotTeamChatSender` from Task 1 (`using RustPlusBot.Features.Connections.Listening;` already present).
- Produces: alarm-trigger lines reach the game as `[R+] <line>`; `AlarmRelayChannels` is DI-constructed (`AddSingleton<AlarmRelayChannels>()` in `AlarmServiceCollectionExtensions.cs:27`), so it re-resolves automatically.

- [ ] **Step 1: Swap the record property type**

In `src/RustPlusBot.Features.Alarms/Relaying/AlarmStateRelay.cs` change the `AlarmRelayChannels` record:

```csharp
internal sealed record AlarmRelayChannels(
    IAlarmChannelLocator Locator,
    IAlarmChannelPoster Poster,
    IBotTeamChatSender TeamChatSender);
```

(Property name and `<param>` doc stay the same; only the type changes.)

- [ ] **Step 2: Update the three test files mechanically**

In `AlarmStateRelayTests.cs`, `AlarmRegistrationTests.cs`, and `AlarmsHostedServiceTests.cs`, replace every occurrence of the token `ITeamChatSender` with `IBotTeamChatSender` (substitutes — including the non-generic `services.AddSingleton(Substitute.For<IBotTeamChatSender>())` form in `AlarmRegistrationTests.cs:53` — and record/tuple member types like `AlarmStateRelayTests.cs:465`). No other edits.

- [ ] **Step 3: Run the Alarms test project**

Run: `dotnet test tests/RustPlusBot.Features.Alarms.Tests -maxcpucount:1`
Expected: PASS, zero failures.

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Alarms tests/RustPlusBot.Features.Alarms.Tests
git commit -m "feat(alarms): relay alarm lines via IBotTeamChatSender

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Full-solution gate (build, all tests, format)

**Files:**
- Modify: none expected; only whatever `jb cleanupcode` reformats.

**Interfaces:**
- Consumes: everything above.
- Produces: a branch that passes the exact CI gates.

- [ ] **Step 1: Build the whole solution (Release, like CI)**

Run: `dotnet build --configuration Release -maxcpucount:1`
Expected: Build succeeded, 0 warnings-as-errors failures.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test --no-build --configuration Release -maxcpucount:1`
Expected: all test projects PASS, zero failures (baseline before this change: ~797 tests green).

- [ ] **Step 3: Run the format gate**

Run: `dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder" --no-build --verbosity=ERROR`
Then: `git status --short`
Expected: empty output (no diff). If files were reformatted, inspect with `git diff`, then:

```bash
git add -u
git commit -m "style: jb cleanupcode pass

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 4: Confirm the branch is clean and pushed nowhere yet**

Run: `git log --oneline develop..HEAD` — expect the 5–7 commits from Tasks 1–6 (+ optional style commit).
Do NOT push or open a PR in this task; hand off via superpowers:finishing-a-development-branch.
