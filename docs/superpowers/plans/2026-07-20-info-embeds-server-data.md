# #info Server-Data Embeds Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the nine server-data slash commands and surface their data — plus more — as three auto-refreshing embeds (Server / Events / Team) in each server's `#info` channel.

**Architecture:** The three embeds are `IMessageRenderer` implementations wired into the existing Workspace reconciler via `MessageSpec` declarations. Each renderer lives in the project that owns its data source. A new hosted service ticks on a configurable interval and drives a narrow, render-gated refresh path that bypasses full channel reconciliation. The "switch active player" select moves off the embed into a new `/server player` command.

**Tech Stack:** .NET 10, C# 13, Discord.Net (`Discord.Interactions`), EF Core 10, xUnit + NSubstitute, Serilog.

## Global Constraints

- Solution file is `RustPlusBot.slnx` — there is **no** `.sln`. Build with `dtk build RustPlusBot.slnx`.
- `dotnet jb cleanupcode --profile=ReformatAndReorder` is a hard CI gate that fails on any diff. Run it before the final commit of each task.
- Every new user-facing string is a resx key present in **both** `src/RustPlusBot.Localization/Strings.resx` and `Strings.fr.resx`. `StringsResourceParityTests` enforces parity and a hard-coded key count.
- The key count assertion lives at `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs:44` and currently reads `Assert.Equal(281, EnglishKeys().Count);`. Update it in whichever task changes the catalog.
- Public types and members require XML doc comments (the repo builds with docs enabled); match the surrounding density.
- Never bump ImageSharp to v4 or SixLabors.Drawing to v3 — the paid license hard-fails the build.
- Work happens on branch `feat/info-embeds`, already cut off `develop` with the spec committed.
- Spec: `docs/superpowers/specs/2026-07-20-info-embeds-server-data-design.md`.

---

## File Structure

**Task 1 — shared formatting primitives (unblocks everything else)**
- Create `src/RustPlusBot.Abstractions/Formatting/DurationFormat.cs` — public duration formatting.
- Create `src/RustPlusBot.Abstractions/Connections/Daylight.cs` — day/night + next-transition over `ServerTimeSnapshot`.
- Delete `src/RustPlusBot.Features.Commands/Formatting/DurationFormat.cs`.

**Task 2 — open the renderer seam**
- Modify `src/RustPlusBot.Features.Workspace/Registry/IMessageRenderer.cs`, `Registry/MessageRenderContext.cs`, `Gateway/MessagePayload.cs` — `internal` → `public`.

**Task 3 — Server embed**
- Modify `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs`.

**Task 4 — Events embed**
- Create `src/RustPlusBot.Features.Events/Messages/ServerEventsMessageRenderer.cs`.

**Task 5 — Team embed**
- Create `src/RustPlusBot.Features.Players/Messages/ServerTeamMessageRenderer.cs`.

**Task 6 — declare the specs**
- Modify `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`, `Specs/ServerWorkspaceSpecProvider.cs`.

**Task 7 — gated refresh path**
- Create `src/RustPlusBot.Features.Workspace/Reconciler/ServerInfoRefresher.cs` (+ `IServerInfoRefresher`).

**Task 8 — refresh hosted service**
- Create `src/RustPlusBot.Features.Workspace/Hosting/ServerInfoRefreshHostedService.cs`.
- Modify `src/RustPlusBot.Features.Workspace/WorkspaceOptions.cs`, `src/RustPlusBot.Host/appsettings.json`.

**Task 9 — delete the slash commands, move the resolver**
- Delete `src/RustPlusBot.Features.Commands/Modules/ServerCommandModule.cs`, `Servers/ServerQueryService.cs`.
- Move `Servers/ServerResolver.cs`, `Servers/ServerResolution.cs`, `Servers/ServerAutocompleteHandler.cs` → `src/RustPlusBot.Features.Connections/Servers/`.

**Task 10 — `/server player`**
- Create `src/RustPlusBot.Features.Connections/Modules/ServerPlayerModule.cs`.
- Modify `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`.

---

### Task 1: Shared formatting primitives

`DurationFormat` is `internal` to Features.Commands — the highest project in the reference graph — but all three renderers sit below it. Move it to Abstractions. Same for the day/night math currently inlined in `TimeCommandHandler`.

**Files:**
- Create: `src/RustPlusBot.Abstractions/Formatting/DurationFormat.cs`
- Create: `src/RustPlusBot.Abstractions/Connections/Daylight.cs`
- Delete: `src/RustPlusBot.Features.Commands/Formatting/DurationFormat.cs`
- Modify: `src/RustPlusBot.Features.Commands/Handlers/TimeCommandHandler.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/Connections/DaylightTests.cs`

**Interfaces:**
- Consumes: `ServerTimeSnapshot(float TimeOfDay, float Sunrise, float Sunset)` from `RustPlusBot.Abstractions.Connections`.
- Produces:
  - `RustPlusBot.Abstractions.Formatting.DurationFormat.Compact(TimeSpan) -> string`
  - `RustPlusBot.Abstractions.Formatting.DurationFormat.Seconds(double) -> string`
  - `RustPlusBot.Abstractions.Connections.Daylight.IsDay(ServerTimeSnapshot) -> bool`
  - `RustPlusBot.Abstractions.Connections.Daylight.Clock(ServerTimeSnapshot) -> string` (e.g. `"14:32"`)
  - `RustPlusBot.Abstractions.Connections.Daylight.HoursUntilTransition(ServerTimeSnapshot) -> float` (in-game hours until the next sunrise/sunset)
  - `RustPlusBot.Abstractions.Connections.Daylight.UntilTransition(ServerTimeSnapshot) -> TimeSpan` (the same interval as a `TimeSpan`, for `DurationFormat.Compact`)

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Abstractions.Tests/Connections/DaylightTests.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Tests.Connections;

public sealed class DaylightTests
{
    [Theory]
    [InlineData(12.0f, true)]   // midday, between sunrise and sunset
    [InlineData(7.5f, true)]    // just after sunrise
    [InlineData(2.0f, false)]   // pre-dawn
    [InlineData(21.0f, false)]  // after sunset
    [InlineData(7.0f, true)]    // exactly sunrise counts as day
    [InlineData(20.0f, false)]  // exactly sunset counts as night
    public void IsDay_bins_against_sunrise_and_sunset(float timeOfDay, bool expected)
    {
        var snapshot = new ServerTimeSnapshot(timeOfDay, 7.0f, 20.0f);

        Assert.Equal(expected, Daylight.IsDay(snapshot));
    }

    [Theory]
    [InlineData(14.53f, "14:31")]
    [InlineData(0.0f, "00:00")]
    [InlineData(9.5f, "09:30")]
    [InlineData(23.99f, "23:59")]
    public void Clock_formats_as_zero_padded_hours_and_minutes(float timeOfDay, string expected)
    {
        var snapshot = new ServerTimeSnapshot(timeOfDay, 7.0f, 20.0f);

        Assert.Equal(expected, Daylight.Clock(snapshot));
    }

    [Fact]
    public void HoursUntilTransition_during_day_counts_to_sunset()
    {
        var snapshot = new ServerTimeSnapshot(14.0f, 7.0f, 20.0f);

        Assert.Equal(6.0f, Daylight.HoursUntilTransition(snapshot), 3);
    }

    [Fact]
    public void HoursUntilTransition_before_sunrise_counts_to_sunrise()
    {
        var snapshot = new ServerTimeSnapshot(2.0f, 7.0f, 20.0f);

        Assert.Equal(5.0f, Daylight.HoursUntilTransition(snapshot), 3);
    }

    [Fact]
    public void HoursUntilTransition_after_sunset_wraps_past_midnight_to_sunrise()
    {
        var snapshot = new ServerTimeSnapshot(22.0f, 7.0f, 20.0f);

        // 2h to midnight + 7h to sunrise.
        Assert.Equal(9.0f, Daylight.HoursUntilTransition(snapshot), 3);
    }

    [Fact]
    public void UntilTransition_matches_HoursUntilTransition()
    {
        var snapshot = new ServerTimeSnapshot(14.0f, 7.0f, 20.0f);

        Assert.Equal(TimeSpan.FromHours(6.0), Daylight.UntilTransition(snapshot), TimeSpan.FromSeconds(1));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk test tests/RustPlusBot.Abstractions.Tests --filter FullyQualifiedName~DaylightTests`
Expected: FAIL — compile error, `The name 'Daylight' does not exist in the namespace`.

If `tests/RustPlusBot.Abstractions.Tests` does not exist, create it by copying the csproj from `tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj`, renaming the assembly and swapping the `ProjectReference` to `../../src/RustPlusBot.Abstractions/RustPlusBot.Abstractions.csproj`, then add it to `RustPlusBot.slnx` beside the other test projects.

- [ ] **Step 3: Create the Daylight helper**

Create `src/RustPlusBot.Abstractions/Connections/Daylight.cs`:

```csharp
using System.Globalization;

namespace RustPlusBot.Abstractions.Connections;

/// <summary>
///     Day/night reasoning over a <see cref="ServerTimeSnapshot" />. Shared by the in-game !time
///     handler and the #info server embed so the two can never disagree. All intervals are in
///     <em>in-game</em> hours: Rust's day length is server-configurable and the API never reports
///     it, so these values must not be presented as real-world minutes.
/// </summary>
public static class Daylight
{
    private const float HoursPerDay = 24f;

    /// <summary>True when the in-game clock sits between sunrise (inclusive) and sunset (exclusive).</summary>
    /// <param name="snapshot">The in-game time snapshot.</param>
    /// <returns>True during daylight; false at night.</returns>
    public static bool IsDay(ServerTimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.TimeOfDay >= snapshot.Sunrise && snapshot.TimeOfDay < snapshot.Sunset;
    }

    /// <summary>Renders the in-game clock as zero-padded "HH:mm".</summary>
    /// <param name="snapshot">The in-game time snapshot.</param>
    /// <returns>The clock text, e.g. "14:32".</returns>
    public static string Clock(ServerTimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var hours = (int)snapshot.TimeOfDay;
        var minutes = (int)((snapshot.TimeOfDay - hours) * 60f);
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}");
    }

    /// <summary>In-game hours until the next sunrise or sunset, wrapping past midnight.</summary>
    /// <param name="snapshot">The in-game time snapshot.</param>
    /// <returns>The interval in in-game hours; never negative.</returns>
    public static float HoursUntilTransition(ServerTimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var target = IsDay(snapshot) ? snapshot.Sunset : snapshot.Sunrise;
        var delta = target - snapshot.TimeOfDay;
        return delta >= 0f ? delta : delta + HoursPerDay;
    }

    /// <summary>The same interval as <see cref="HoursUntilTransition" />, as a <see cref="TimeSpan" />.</summary>
    /// <param name="snapshot">The in-game time snapshot.</param>
    /// <returns>The interval in in-game hours expressed as a TimeSpan.</returns>
    public static TimeSpan UntilTransition(ServerTimeSnapshot snapshot)
        => TimeSpan.FromHours(HoursUntilTransition(snapshot));
}
```

- [ ] **Step 4: Move DurationFormat to Abstractions**

Create `src/RustPlusBot.Abstractions/Formatting/DurationFormat.cs` with the exact body of the existing file, changing only the namespace and visibility:

```csharp
using System.Globalization;

namespace RustPlusBot.Abstractions.Formatting;

/// <summary>Formats durations compactly for in-game replies and Discord embeds.</summary>
public static class DurationFormat
{
    /// <summary>Renders a duration as "Xd Yh" / "Yh Zm" / "Zm".</summary>
    /// <param name="span">The duration to render.</param>
    /// <returns>A compact duration string.</returns>
    public static string Compact(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours}h");
        }

        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes}m");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m");
    }

    /// <summary>Renders a sub-minute-aware duration: "&lt;n&gt;s" under a minute, else "&lt;m&gt;m &lt;s&gt;s".</summary>
    /// <param name="seconds">The duration in seconds.</param>
    /// <returns>A compact duration string.</returns>
    public static string Seconds(double seconds)
    {
        if (seconds < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds:0.#}s");
        }

        var span = TimeSpan.FromSeconds(seconds);
        var minutes = (int)span.TotalMinutes;
        return span.Seconds == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes}m")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}m {span.Seconds}s");
    }
}
```

Then delete the old file:

```bash
rm src/RustPlusBot.Features.Commands/Formatting/DurationFormat.cs
```

- [ ] **Step 5: Repoint the thirteen call sites**

Every consumer is inside Features.Commands and needs only a `using` swap. In each of these files, replace `using RustPlusBot.Features.Commands.Formatting;` with `using RustPlusBot.Abstractions.Formatting;` — or, where the file needs *both* namespaces (it uses another helper from `Features.Commands.Formatting` too), add the Abstractions using alongside it:

```
src/RustPlusBot.Features.Commands/Formatting/ItemLine.cs
src/RustPlusBot.Features.Commands/Formatting/DecayLine.cs
src/RustPlusBot.Features.Commands/Formatting/DurabilityLine.cs
src/RustPlusBot.Features.Commands/Formatting/SmeltLine.cs
src/RustPlusBot.Features.Commands/Handlers/AfkCommandHandler.cs
src/RustPlusBot.Features.Commands/Handlers/AliveCommandHandler.cs
src/RustPlusBot.Features.Commands/Handlers/RigReply.cs
src/RustPlusBot.Features.Commands/Handlers/UptimeCommandHandler.cs
src/RustPlusBot.Features.Commands/Handlers/WipeCommandHandler.cs
src/RustPlusBot.Features.Commands/Handlers/MarkerReply.cs
src/RustPlusBot.Features.Commands/Modules/CommandSurfaceModule.cs
src/RustPlusBot.Features.Commands/Modules/DiagnosticsModule.cs
```

Note the four files under `Formatting/` are themselves in namespace `RustPlusBot.Features.Commands.Formatting`, so they resolved `DurationFormat` with no using at all — those need the Abstractions using **added**.

Verify nothing was missed:

```bash
grep -rn "DurationFormat" src --include='*.cs' | grep -v "/obj/\|/bin/" | grep -v "Abstractions/Formatting"
```

Expected: every hit is a call site, none is a namespace declaration.

- [ ] **Step 6: Rewrite TimeCommandHandler over the shared helper**

Replace the body of `src/RustPlusBot.Features.Commands/Handlers/TimeCommandHandler.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!time — reports the in-game clock and whether it is day or night.</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class TimeCommandHandler(IRustServerQuery query, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "time";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var time = await query.GetTimeAsync(context.GuildId, context.ServerId, cancellationToken).ConfigureAwait(false);
        if (time is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        var phase = localizer.Get(Daylight.IsDay(time) ? "command.time.day" : "command.time.night", context.Culture);

        return localizer.Get("command.time.ok", context.Culture, Daylight.Clock(time), phase);
    }
}
```

- [ ] **Step 7: Run the tests**

Run: `dtk test tests/RustPlusBot.Abstractions.Tests --filter FullyQualifiedName~DaylightTests`
Expected: PASS — all 13 test cases.

Run: `dtk test tests/RustPlusBot.Features.Commands.Tests`
Expected: PASS — the existing `!time` and duration-formatting tests still pass unchanged, proving the move was behaviour-preserving.

- [ ] **Step 8: Format and commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "refactor: move DurationFormat to Abstractions and extract Daylight helper

Both are needed by the #info renderers, which sit below Features.Commands
in the project graph. Behaviour is unchanged; TimeCommandHandler now shares
its day/night math with the forthcoming server embed."
```

---

### Task 2: Open the renderer seam

The Events and Team renderers live outside Features.Workspace, so the three contracts they implement must become public.

**Files:**
- Modify: `src/RustPlusBot.Features.Workspace/Registry/IMessageRenderer.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Registry/MessageRenderContext.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Gateway/MessagePayload.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `public interface IMessageRenderer { string MessageKey { get; } ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken); }`, `public sealed record MessageRenderContext(ulong GuildId, Guid? ServerId, string Culture)`, `public sealed record MessagePayload(string? Text, Embed? Embed, MessageComponent? Components)` — all in their existing namespaces (`RustPlusBot.Features.Workspace.Registry` and `...Gateway`).

- [ ] **Step 1: Widen the three declarations**

In `src/RustPlusBot.Features.Workspace/Registry/IMessageRenderer.cs`, change:

```csharp
internal interface IMessageRenderer
```

to:

```csharp
public interface IMessageRenderer
```

In `src/RustPlusBot.Features.Workspace/Registry/MessageRenderContext.cs`, change:

```csharp
internal sealed record MessageRenderContext(ulong GuildId, Guid? ServerId, string Culture);
```

to:

```csharp
public sealed record MessageRenderContext(ulong GuildId, Guid? ServerId, string Culture);
```

In `src/RustPlusBot.Features.Workspace/Gateway/MessagePayload.cs`, change:

```csharp
internal sealed record MessagePayload(string? Text, Embed? Embed, MessageComponent? Components);
```

to:

```csharp
public sealed record MessagePayload(string? Text, Embed? Embed, MessageComponent? Components);
```

- [ ] **Step 2: Build to surface accessibility fallout**

Run: `dtk build RustPlusBot.slnx`
Expected: PASS. A public interface may not expose less-accessible types in its signature — `MessagePayload` and `MessageRenderContext` are widened in the same step, so `IMessageRenderer` is consistent.

If the build reports CS0051/CS0053 ("inconsistent accessibility") on any *other* type reachable from these signatures, widen that type too and note it in the commit message.

- [ ] **Step 3: Run the Workspace tests**

Run: `dtk test tests/RustPlusBot.Features.Workspace.Tests`
Expected: PASS — visibility widening is behaviour-neutral.

- [ ] **Step 4: Format and commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "refactor: make the workspace message-renderer contracts public

Lets Features.Events and Features.Players contribute renderers for the new
#info embeds without moving the state interfaces into Abstractions."
```

---

### Task 3: Server embed

Rewrite the existing `#info` status embed: add Players / Time / Wipe, drop the swap select, keep the Remove button.

**Files:**
- Modify: `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `src/RustPlusBot.Localization/Strings.fr.resx`
- Modify: `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs:44`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`

**Interfaces:**
- Consumes: `Daylight.IsDay/Clock/UntilTransition`, `DurationFormat.Compact` (Task 1); `MessagePayload`, `MessageRenderContext`, `IMessageRenderer` (Task 2).
- Produces: `ServerInfoMessageRenderer(IServerService servers, IConnectionStore connections, IRustServerQuery query, IClock clock, ILocalizer localizer)` — note the **added `IClock` parameter**, which Tasks 4 and 5 mirror.

- [ ] **Step 1: Add the resx keys**

Add to `src/RustPlusBot.Localization/Strings.resx`:

| Key | Value |
| --- | ----- |
| `server.info.players.value` | `{0} / {1} · {2} queued` |
| `server.info.time.label` | `Time` |
| `server.info.time.day` | `☀️` |
| `server.info.time.night` | `🌙` |
| `server.info.time.value` | `{0} {1} · {2} of in-game time to {3}` |
| `server.info.time.to.day` | `sunrise` |
| `server.info.time.to.night` | `nightfall` |
| `server.info.wipe.label` | `Wipe` |
| `server.info.wipe.value` | `{0} ago` |
| `server.info.wipe.unknown` | `Unknown` |

And the French counterparts in `Strings.fr.resx`:

| Key | Value |
| --- | ----- |
| `server.info.players.value` | `{0} / {1} · {2} en file` |
| `server.info.time.label` | `Heure` |
| `server.info.time.day` | `☀️` |
| `server.info.time.night` | `🌙` |
| `server.info.time.value` | `{0} {1} · {2} de temps en jeu avant {3}` |
| `server.info.time.to.day` | `le lever du soleil` |
| `server.info.time.to.night` | `la tombée de la nuit` |
| `server.info.wipe.label` | `Wipe` |
| `server.info.wipe.value` | `il y a {0}` |
| `server.info.wipe.unknown` | `Inconnu` |

That is **10 new keys**. Update `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs:44` from `Assert.Equal(281, EnglishKeys().Count);` to `Assert.Equal(291, EnglishKeys().Count);`.

- [ ] **Step 2: Write the failing tests**

Add to `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`. These use the existing file's `Loc` field and NSubstitute idiom:

```csharp
    [Fact]
    public async Task ServerInfo_Connected_ShowsPlayersTimeAndWipe()
    {
        var serverId = Guid.NewGuid();
        var credId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

        var servers = Substitute.For<IServerService>();
        servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = serverId, GuildId = 1, Name = "Rustopia EU", Ip = "1.2.3.4", Port = 28015
            });

        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new DomainConnectionState
            {
                RustServerId = serverId,
                GuildId = 1,
                ActiveCredentialId = credId,
                Status = ConnectionStatus.Connected,
                PlayerCount = 187,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns([
                new()
                {
                    Id = credId, GuildId = 1, RustServerId = serverId, OwnerUserId = 7,
                    SteamId = 76561198000000000UL, Status = CredentialStatus.Active
                },
            ]);

        var query = Substitute.For<IRustServerQuery>();
        query.GetServerInfoAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new ServerInfoSnapshot(187, 200, 12, now.AddDays(-4).AddHours(-6)));
        query.GetTimeAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new ServerTimeSnapshot(14.0f, 7.0f, 20.0f));
        query.GetTeamInfoAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, clock, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var values = string.Concat(payload.Embed!.Fields.Select(f => f.Value));
        Assert.Contains("187 / 200", values, StringComparison.Ordinal);
        Assert.Contains("12 queued", values, StringComparison.Ordinal);
        Assert.Contains("14:00", values, StringComparison.Ordinal);
        Assert.Contains("4d 6h ago", values, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerInfo_NoLongerRendersSwapSelect()
    {
        var serverId = Guid.NewGuid();
        var credId = Guid.NewGuid();

        var servers = Substitute.For<IServerService>();
        servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = serverId, GuildId = 1, Name = "Rustopia EU", Ip = "1.2.3.4", Port = 28015
            });

        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new DomainConnectionState
            {
                RustServerId = serverId, GuildId = 1, ActiveCredentialId = credId,
                Status = ConnectionStatus.Connected, PlayerCount = 1,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns([
                new()
                {
                    Id = credId, GuildId = 1, RustServerId = serverId, OwnerUserId = 7,
                    SteamId = 76561198000000000UL, Status = CredentialStatus.Active
                },
            ]);

        var query = Substitute.For<IRustServerQuery>();
        query.GetServerInfoAsync(1, serverId, Arg.Any<CancellationToken>()).Returns((ServerInfoSnapshot?)null);
        query.GetTimeAsync(1, serverId, Arg.Any<CancellationToken>()).Returns((ServerTimeSnapshot?)null);
        query.GetTeamInfoAsync(1, serverId, Arg.Any<CancellationToken>()).Returns((TeamInfoSnapshot?)null);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, clock, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var selects = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<SelectMenuComponent>();
        Assert.Empty(selects);

        var buttons = payload.Components.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>();
        Assert.Contains(buttons, b => b.CustomId == $"workspace:info:remove:{serverId}");
    }

    [Fact]
    public async Task ServerInfo_Disconnected_OmitsLiveFields()
    {
        var serverId = Guid.NewGuid();

        var servers = Substitute.For<IServerService>();
        servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = serverId, GuildId = 1, Name = "Rustopia EU", Ip = "1.2.3.4", Port = 28015
            });

        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new DomainConnectionState
            {
                RustServerId = serverId, GuildId = 1, Status = ConnectionStatus.Unreachable,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>()).Returns([]);

        var query = Substitute.For<IRustServerQuery>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, clock, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        // Disconnected: never queries the socket, and shows only Status + Active player.
        await query.DidNotReceive().GetServerInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Equal(2, payload.Embed!.Fields.Length);
    }
```

Add these usings to the top of the file if not already present:

```csharp
using RustPlusBot.Abstractions.Time;
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dtk test tests/RustPlusBot.Features.Workspace.Tests --filter FullyQualifiedName~RendererTests`
Expected: FAIL — compile error, `ServerInfoMessageRenderer` has no 5-parameter constructor.

- [ ] **Step 4: Rewrite the renderer**

Replace `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs` entirely:

```csharp
using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders a server's #info status embed: connection, population, in-game time and wipe age.</summary>
/// <param name="servers">Server lookup.</param>
/// <param name="connections">Live connection state + pool.</param>
/// <param name="query">Live server query.</param>
/// <param name="clock">Supplies the current time for the wipe age.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class ServerInfoMessageRenderer(
    IServerService servers,
    IConnectionStore connections,
    IRustServerQuery query,
    IClock clock,
    ILocalizer localizer) : IMessageRenderer
{
    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.ServerInfo;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return new MessagePayload(null, null, null);
        }

        var server = await servers.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            return new MessagePayload(null, null, null);
        }

        var state = await connections.GetStateAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var pool = await connections.ListPoolAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var status = state?.Status ?? ConnectionStatus.NoCredentials;

        var active = pool.FirstOrDefault(c => state?.ActiveCredentialId is Guid id && c.Id == id)
                     ?? pool.FirstOrDefault(c => c.Status == CredentialStatus.Active);
        var none = localizer.Get("server.info.none", context.Culture);
        var port = server.Port.ToString(CultureInfo.InvariantCulture);

        var embed = new EmbedBuilder()
            .WithTitle($"{Glyph(status)} {server.Name}")
            .WithDescription(localizer.Get("server.info.endpoint", context.Culture, server.Ip, port))
            .WithColor(ColorFor(status))
            .AddField(localizer.Get("server.info.status.label", context.Culture), StatusText(status, context.Culture))
            .AddField(localizer.Get("server.info.player.label", context.Culture),
                active is null ? none : active.SteamId.ToString(CultureInfo.InvariantCulture));

        if (status == ConnectionStatus.Connected)
        {
            await AddLiveFieldsAsync(embed, context, serverId, cancellationToken).ConfigureAwait(false);
        }

        // The swap select moved to /server player; the remove button now owns row 0 alone.
        var builder = new ComponentBuilder()
            .WithButton(
                localizer.Get("server.info.remove.button", context.Culture),
                $"{WorkspaceComponentIds.ServerInfoRemovePrefix}{serverId}",
                ButtonStyle.Danger,
                row: 0);

        return new MessagePayload(null, embed.Build(), builder.Build());
    }

    private async ValueTask AddLiveFieldsAsync(
        EmbedBuilder embed,
        MessageRenderContext context,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        var info = await query.GetServerInfoAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (info is not null)
        {
            embed.AddField(
                localizer.Get("server.info.players.label", context.Culture),
                localizer.Get("server.info.players.value", context.Culture,
                    info.Players.ToString(CultureInfo.InvariantCulture),
                    info.MaxPlayers.ToString(CultureInfo.InvariantCulture),
                    info.QueuedPlayers.ToString(CultureInfo.InvariantCulture)));
        }

        var time = await query.GetTimeAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (time is not null)
        {
            var isDay = Daylight.IsDay(time);
            embed.AddField(
                localizer.Get("server.info.time.label", context.Culture),
                localizer.Get("server.info.time.value", context.Culture,
                    Daylight.Clock(time),
                    localizer.Get(isDay ? "server.info.time.day" : "server.info.time.night", context.Culture),
                    DurationFormat.Compact(Daylight.UntilTransition(time)),
                    localizer.Get(isDay ? "server.info.time.to.night" : "server.info.time.to.day", context.Culture)));
        }

        if (info is not null)
        {
            embed.AddField(
                localizer.Get("server.info.wipe.label", context.Culture),
                info.WipeTimeUtc is { } wiped
                    ? localizer.Get("server.info.wipe.value", context.Culture,
                        DurationFormat.Compact(clock.UtcNow - wiped))
                    : localizer.Get("server.info.wipe.unknown", context.Culture));
        }
    }

    private static string Glyph(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => "🟢",
        ConnectionStatus.Connecting or ConnectionStatus.Unreachable => "🟡",
        _ => "🔴",
    };

    private static Color ColorFor(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => Color.Green,
        ConnectionStatus.Connecting or ConnectionStatus.Unreachable => Color.Gold,
        _ => Color.Red,
    };

    private string StatusText(ConnectionStatus status, string culture) => status switch
    {
        ConnectionStatus.Connecting => localizer.Get("server.info.status.connecting", culture),
        ConnectionStatus.Connected => localizer.Get("server.info.status.connected", culture),
        ConnectionStatus.Unreachable => localizer.Get("server.info.status.unreachable", culture),
        _ => localizer.Get("server.info.status.nocredentials", culture),
    };
}
```

The old `AddTeamFieldAsync` is gone — the team moves to its own embed in Task 5.

- [ ] **Step 5: Delete the now-stale assertions**

Existing tests in `RendererTests.cs` assert the swap select and the two-row layout. Find them:

```bash
grep -n "swap\|SwapSelect\|SeparateActionRows\|HasRemoveServerButton" \
  tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs
```

Delete every test method that grep implicates. At time of writing those are
`ServerInfo_Connected_ShowsStatusActivePlayerAndCount_AndSwapSelect`,
`ServerInfo_HasRemoveServerButton`, and the method asserting the select and button sit in separate
action rows. `ServerInfo_NoLongerRendersSwapSelect` from Step 2 covers the remove button, so no
coverage is lost.

Every remaining `new ServerInfoMessageRenderer(...)` call in the file needs the new `clock` argument added.

- [ ] **Step 6: Run the tests**

Run: `dtk test tests/RustPlusBot.Features.Workspace.Tests --filter FullyQualifiedName~RendererTests`
Expected: PASS.

Run: `dtk test tests/RustPlusBot.Localization.Tests`
Expected: PASS — parity holds and the count is 291.

- [ ] **Step 7: Format and commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "feat: add players, time and wipe to the #info server embed

Renders the MaxPlayers/QueuedPlayers/WipeTimeUtc already carried by
ServerInfoSnapshot but never shown, replacing /pop, /time and /wipe.
Drops the swap select ahead of /server player replacing it."
```

---

### Task 4: Events embed

**Files:**
- Create: `src/RustPlusBot.Features.Events/Messages/ServerEventsMessageRenderer.cs`
- Modify: `src/RustPlusBot.Features.Events/EventServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `Strings.fr.resx`
- Modify: `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs:44`
- Test: `tests/RustPlusBot.Features.Events.Tests/Messages/ServerEventsMessageRendererTests.cs`

**Interfaces:**
- Consumes: `DurationFormat.Compact` (Task 1); `IMessageRenderer`, `MessagePayload`, `MessageRenderContext` (Task 2); `IEventState.GetActiveMarkers(ulong, Guid, MarkerKind) -> IReadOnlyList<ActiveMarker>`; `IRigState.Get(ulong, Guid, RigKind) -> RigState`; `GridReference.From(float, float, MapDimensions?, MapGridStyle) -> string`; `IConnectionStore.GetStateAsync`.
- Produces: `ServerEventsMessageRenderer(IEventState events, IRigState rigs, IMapSettingsStore mapSettings, IConnectionStore connections, IClock clock, ILocalizer localizer)` with `MessageKey => "server.events"`.

Note the `IConnectionStore` dependency: marker and rig state are in-process and are cleared on
disconnect, so a disconnected server would otherwise render as a confident "Not out / Online" wall.
The renderer stamps the disconnected notice into the embed description instead.

- [ ] **Step 1: Add the resx keys**

Add to `Strings.resx`:

| Key | Value |
| --- | ----- |
| `server.events.title` | `⚡ Events` |
| `server.events.cargo.label` | `🚢 Cargo Ship` |
| `server.events.heli.label` | `🚁 Patrol Helicopter` |
| `server.events.chinook.label` | `🚁 Chinook` |
| `server.events.small.label` | `🛢️ Small Oil Rig` |
| `server.events.large.label` | `🛢️ Large Oil Rig` |
| `server.events.out` | `Out · {0} · {1} ago` |
| `server.events.notout` | `Not out` |
| `server.events.rig.online` | `Online` |
| `server.events.rig.active` | `Crate unlocking · {0} left` |
| `server.events.rig.offline` | `Looted · respawns in {0}` |
| `server.events.disconnected` | `Not connected to the server.` |

And `Strings.fr.resx`:

| Key | Value |
| --- | ----- |
| `server.events.title` | `⚡ Événements` |
| `server.events.cargo.label` | `🚢 Cargo` |
| `server.events.heli.label` | `🚁 Hélicoptère de patrouille` |
| `server.events.chinook.label` | `🚁 Chinook` |
| `server.events.small.label` | `🛢️ Petite plateforme` |
| `server.events.large.label` | `🛢️ Grande plateforme` |
| `server.events.out` | `Présent · {0} · il y a {1}` |
| `server.events.notout` | `Absent` |
| `server.events.rig.online` | `En ligne` |
| `server.events.rig.active` | `Caisse en déverrouillage · {0} restant` |
| `server.events.rig.offline` | `Pillée · réapparaît dans {0}` |
| `server.events.disconnected` | `Non connecté au serveur.` |

That is **12 new keys**: update the count assertion from `291` to `303`.

- [ ] **Step 2: Write the failing test**

Create `tests/RustPlusBot.Features.Events.Tests/Messages/ServerEventsMessageRendererTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Events.Messages;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Events.Tests.Messages;

public sealed class ServerEventsMessageRendererTests
{
    private static readonly ResxLocalizer Loc = new();
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private static ServerEventsMessageRenderer Build(
        IEventState events,
        IRigState rigs,
        ConnectionStatus status = ConnectionStatus.Connected)
    {
        var mapSettings = Substitute.For<IMapSettingsStore>();
        mapSettings.GetAsync(1, ServerId, Arg.Any<CancellationToken>()).Returns(MapLayerSettings.AllOn);
        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = 1, RustServerId = ServerId, Status = status
            });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ServerEventsMessageRenderer(events, rigs, mapSettings, connections, clock, Loc);
    }

    [Fact]
    public async Task Renders_all_five_rows()
    {
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, Arg.Any<RigKind>()).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.NotNull(payload.Embed);
        Assert.Equal(5, payload.Embed!.Fields.Length);
    }

    [Fact]
    public async Task Absent_marker_renders_not_out()
    {
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, Arg.Any<RigKind>()).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var cargo = payload.Embed!.Fields.Single(f => f.Name.Contains("Cargo", StringComparison.Ordinal));
        Assert.Equal("Not out", cargo.Value);
    }

    [Fact]
    public async Task Present_marker_renders_grid_and_age()
    {
        var dims = new MapDimensions(4000, 0, 0, 4000);
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        events.GetActiveMarkers(1, ServerId, MarkerKind.CargoShip)
            .Returns([
                new ActiveMarker(9UL, MarkerKind.CargoShip, 2000f, 2000f, dims, Now.AddMinutes(-12), [], null),
            ]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, Arg.Any<RigKind>()).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var cargo = payload.Embed!.Fields.Single(f => f.Name.Contains("Cargo", StringComparison.Ordinal));
        Assert.StartsWith("Out ·", cargo.Value, StringComparison.Ordinal);
        Assert.Contains("12m ago", cargo.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RigStatus.Online, "Online")]
    [InlineData(RigStatus.Active, "Crate unlocking · 4m left")]
    [InlineData(RigStatus.Offline, "Looted · respawns in 4m")]
    public async Task Rig_phases_render_distinctly(RigStatus status, string expected)
    {
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, RigKind.Small).Returns(new RigState(status, TimeSpan.FromMinutes(4)));
        rigs.Get(1, ServerId, RigKind.Large).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var small = payload.Embed!.Fields.Single(f => f.Name.Contains("Small", StringComparison.Ordinal));
        Assert.Equal(expected, small.Value);
    }

    [Fact]
    public async Task No_server_scope_renders_empty_payload()
    {
        var events = Substitute.For<IEventState>();
        var rigs = Substitute.For<IRigState>();

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, null, "en"), default);

        Assert.Null(payload.Embed);
        Assert.Null(payload.Text);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Disconnected_says_so_instead_of_reporting_stale_state()
    {
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, Arg.Any<RigKind>()).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs, ConnectionStatus.Unreachable)
            .RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        // In-process marker and rig state is cleared on disconnect, so an unqualified
        // "Not out / Online" wall would read as a confident live report.
        Assert.Equal("Not connected to the server.", payload.Embed!.Description);
    }
}
```

Add `using RustPlusBot.Domain.Connections;` and `using RustPlusBot.Persistence.Connections;` to the test file's usings.

Confirm the `MapDimensions` constructor arity before running — if it differs from `(uint WorldSize, ...)` above, adjust the test's `dims` construction to match `src/RustPlusBot.Abstractions/Connections/MapDimensions.cs`.

- [ ] **Step 3: Run test to verify it fails**

Run: `dtk test tests/RustPlusBot.Features.Events.Tests --filter FullyQualifiedName~ServerEventsMessageRendererTests`
Expected: FAIL — `ServerEventsMessageRenderer` does not exist.

- [ ] **Step 4: Write the renderer**

Create `src/RustPlusBot.Features.Events/Messages/ServerEventsMessageRenderer.cs`:

```csharp
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Events.Messages;

/// <summary>
///     Renders the #info events embed: cargo / patrol heli / chinook presence plus both oil rigs'
///     lifecycle phase. Reads only in-process state, so it costs no Rust+ calls.
/// </summary>
/// <param name="events">Active map-marker state.</param>
/// <param name="rigs">Inferred oil-rig state.</param>
/// <param name="mapSettings">Supplies the per-server grid convention.</param>
/// <param name="connections">Live connection status, to avoid reporting cleared state as live.</param>
/// <param name="clock">Supplies the current time for marker ages.</param>
/// <param name="localizer">String resolution.</param>
public sealed class ServerEventsMessageRenderer(
    IEventState events,
    IRigState rigs,
    IMapSettingsStore mapSettings,
    IConnectionStore connections,
    IClock clock,
    ILocalizer localizer) : IMessageRenderer
{
    /// <summary>The message key this renderer produces.</summary>
    public const string Key = "server.events";

    /// <inheritdoc />
    public string MessageKey => Key;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return new MessagePayload(null, null, null);
        }

        var settings = await mapSettings.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var state = await connections.GetStateAsync(context.GuildId, serverId, cancellationToken)
            .ConfigureAwait(false);
        var connected = state?.Status == ConnectionStatus.Connected;
        var culture = context.Culture;

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("server.events.title", culture))
            .WithColor(connected ? Color.DarkerGrey : Color.Red)
            // Marker and rig state are in-process and cleared on disconnect, so without this the rows
            // would read as a confident live report of an empty world.
            .WithDescription(connected ? null : localizer.Get("server.events.disconnected", culture))
            .AddField(localizer.Get("server.events.cargo.label", culture),
                Marker(context.GuildId, serverId, MarkerKind.CargoShip, settings.GridStyle, culture), inline: true)
            .AddField(localizer.Get("server.events.heli.label", culture),
                Marker(context.GuildId, serverId, MarkerKind.PatrolHelicopter, settings.GridStyle, culture),
                inline: true)
            .AddField(localizer.Get("server.events.chinook.label", culture),
                Marker(context.GuildId, serverId, MarkerKind.Chinook, settings.GridStyle, culture), inline: true)
            .AddField(localizer.Get("server.events.small.label", culture),
                Rig(context.GuildId, serverId, RigKind.Small, culture), inline: true)
            .AddField(localizer.Get("server.events.large.label", culture),
                Rig(context.GuildId, serverId, RigKind.Large, culture), inline: true);

        return new MessagePayload(null, embed.Build(), null);
    }

    private string Marker(ulong guildId, Guid serverId, MarkerKind kind, MapGridStyle style, string culture)
    {
        var active = events.GetActiveMarkers(guildId, serverId, kind);
        if (active.Count == 0)
        {
            return localizer.Get("server.events.notout", culture);
        }

        // Newest-first: the freshest sighting is the one worth reporting.
        var marker = active[0];
        return localizer.Get("server.events.out", culture,
            GridReference.From(marker.X, marker.Y, marker.Dimensions, style),
            DurationFormat.Compact(clock.UtcNow - marker.SeenAtUtc));
    }

    private string Rig(ulong guildId, Guid serverId, RigKind rig, string culture)
    {
        var state = rigs.Get(guildId, serverId, rig);
        return state.Status switch
        {
            RigStatus.Active => localizer.Get("server.events.rig.active", culture,
                DurationFormat.Compact(state.Remaining ?? TimeSpan.Zero)),
            RigStatus.Offline => localizer.Get("server.events.rig.offline", culture,
                DurationFormat.Compact(state.Remaining ?? TimeSpan.Zero)),
            _ => localizer.Get("server.events.rig.online", culture),
        };
    }
}
```

- [ ] **Step 5: Register the renderer**

In `src/RustPlusBot.Features.Events/EventServiceCollectionExtensions.cs`, inside the `AddEvents` method body before `return services;`, add:

```csharp
        // Contributes the #info events embed to the Workspace reconciler.
        services.AddScoped<IMessageRenderer, ServerEventsMessageRenderer>();
```

with these usings at the top of the file:

```csharp
using RustPlusBot.Features.Events.Messages;
using RustPlusBot.Features.Workspace.Registry;
```

- [ ] **Step 6: Run the tests**

Run: `dtk test tests/RustPlusBot.Features.Events.Tests --filter FullyQualifiedName~ServerEventsMessageRendererTests`
Expected: PASS — 8 test cases.

Run: `dtk test tests/RustPlusBot.Localization.Tests`
Expected: PASS at count 303.

- [ ] **Step 7: Format and commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "feat: add the #info events embed

Cargo, patrol heli, chinook and both oil rigs in one embed, replacing
/small and /large. Reads in-process marker and rig state only."
```

---

### Task 5: Team embed

**Files:**
- Create: `src/RustPlusBot.Features.Players/Messages/ServerTeamMessageRenderer.cs`
- Modify: `src/RustPlusBot.Features.Players/PlayerEventServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `Strings.fr.resx`
- Modify: `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs:44`
- Test: `tests/RustPlusBot.Features.Players.Tests/Messages/ServerTeamMessageRendererTests.cs`

**Interfaces:**
- Consumes: `DurationFormat.Compact` (Task 1); `IMessageRenderer`, `MessagePayload`, `MessageRenderContext` (Task 2); `IRustServerQuery.GetTeamInfoAsync`, `GetMapDimensionsAsync`; `IAfkState.GetAfkMembersAsync`; `GridReference.From`.
- Produces: `ServerTeamMessageRenderer(IRustServerQuery query, IAfkState afk, IMapSettingsStore mapSettings, IClock clock, ILocalizer localizer)` with `MessageKey => "server.team"`.

- [ ] **Step 1: Add the resx keys**

Add to `Strings.resx`:

| Key | Value |
| --- | ----- |
| `server.team.title` | `👥 Team — {0}/{1} online` |
| `server.team.line` | `{0}{1} **{2}** · {3} · {4}` |
| `server.team.alive` | `alive {0}` |
| `server.team.afk` | `AFK {0}` |
| `server.team.dead` | `dead {0}` |
| `server.team.offline` | `offline` |
| `server.team.nogrid` | `—` |
| `server.team.empty` | `No team members.` |
| `server.team.disconnected` | `Not connected to the server.` |

`server.team.offline` deliberately carries **no duration placeholder**:
`TeamMemberSnapshot` has `LastSpawnTimeUtc` and `LastDeathTimeUtc` but no disconnect timestamp, so
any "offline for X" figure would be fabricated from the wrong field.

And `Strings.fr.resx`:

| Key | Value |
| --- | ----- |
| `server.team.title` | `👥 Équipe — {0}/{1} en ligne` |
| `server.team.line` | `{0}{1} **{2}** · {3} · {4}` |
| `server.team.alive` | `en vie {0}` |
| `server.team.afk` | `AFK {0}` |
| `server.team.dead` | `mort {0}` |
| `server.team.offline` | `hors ligne` |
| `server.team.nogrid` | `—` |
| `server.team.empty` | `Aucun membre d'équipe.` |
| `server.team.disconnected` | `Non connecté au serveur.` |

That is **9 new keys**: update the count assertion from `303` to `312`.

- [ ] **Step 2: Write the failing test**

Create `tests/RustPlusBot.Features.Players.Tests/Messages/ServerTeamMessageRendererTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Players.Tests.Messages;

public sealed class ServerTeamMessageRendererTests
{
    private static readonly ResxLocalizer Loc = new();
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private static TeamMemberSnapshot Member(
        ulong steamId,
        string name,
        bool online,
        bool alive,
        double spawnedHoursAgo = 2,
        double diedMinutesAgo = 0)
        => new(steamId, name, 2000f, 2000f, online, alive,
            Now.AddHours(-spawnedHoursAgo), Now.AddMinutes(-diedMinutesAgo));

    private static ServerTeamMessageRenderer Build(IRustServerQuery query, IAfkState afk)
    {
        var mapSettings = Substitute.For<IMapSettingsStore>();
        mapSettings.GetAsync(1, ServerId, Arg.Any<CancellationToken>()).Returns(MapLayerSettings.AllOn);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ServerTeamMessageRenderer(query, afk, mapSettings, clock, Loc);
    }

    private static IAfkState NoAfk()
    {
        var afk = Substitute.For<IAfkState>();
        afk.GetAfkMembersAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AfkMember>?>([]));
        return afk;
    }

    [Fact]
    public async Task Title_counts_online_over_total()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(1UL, [
                Member(1UL, "Alice", online: true, alive: true),
                Member(2UL, "Bob", online: true, alive: true),
                Member(3UL, "Carol", online: false, alive: true),
            ]));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Contains("2/3 online", payload.Embed!.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leader_gets_a_crown()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(2UL, [
                Member(1UL, "Alice", online: true, alive: true),
                Member(2UL, "Bob", online: true, alive: true),
            ]));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var bobLine = payload.Embed!.Description.Split('\n').Single(l => l.Contains("Bob", StringComparison.Ordinal));
        Assert.Contains("👑", bobLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Afk_member_renders_afk_not_alive()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(1UL, [Member(1UL, "Alice", online: true, alive: true)]));
        var afk = Substitute.For<IAfkState>();
        afk.GetAfkMembersAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AfkMember>?>(
                [new AfkMember(1UL, "Alice", TimeSpan.FromMinutes(7))]));

        var payload = await Build(query, afk).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Contains("AFK 7m", payload.Embed!.Description, StringComparison.Ordinal);
        Assert.Contains("😴", payload.Embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dead_member_renders_death_age()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(1UL,
                [Member(1UL, "Alice", online: true, alive: false, diedMinutesAgo: 6)]));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Contains("dead 6m", payload.Embed!.Description, StringComparison.Ordinal);
        Assert.Contains("💀", payload.Embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_member_hides_grid()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(1UL, [Member(1UL, "Alice", online: false, alive: true)]));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var line = payload.Embed!.Description.Split('\n').Single(l => l.Contains("Alice", StringComparison.Ordinal));
        Assert.Contains("—", line, StringComparison.Ordinal);
        Assert.Contains("⚫", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_name_falls_back_to_steam_id()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(1UL, [Member(76561198000000000UL, "", online: true, alive: true)]));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Contains("76561198000000000", payload.Embed!.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zero_members_renders_empty_notice()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(0UL, []));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Contains("0/0 online", payload.Embed!.Title, StringComparison.Ordinal);
        Assert.Equal("No team members.", payload.Embed.Description);
    }

    [Fact]
    public async Task Null_snapshot_renders_disconnected_notice()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Equal("Not connected to the server.", payload.Embed!.Description);
    }

    [Fact]
    public async Task Online_alive_sort_before_afk_dead_and_offline()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(99UL, [
                Member(4UL, "Dave", online: false, alive: true),
                Member(3UL, "Carol", online: true, alive: false, diedMinutesAgo: 6),
                Member(2UL, "Bob", online: true, alive: true, spawnedHoursAgo: 1),
                Member(1UL, "Alice", online: true, alive: true, spawnedHoursAgo: 3),
            ]));
        var afk = Substitute.For<IAfkState>();
        afk.GetAfkMembersAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AfkMember>?>(
                [new AfkMember(2UL, "Bob", TimeSpan.FromMinutes(7))]));

        var payload = await Build(query, afk).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var names = payload.Embed!.Description.Split('\n')
            .Select(l => new[] { "Alice", "Bob", "Carol", "Dave" }.First(n => l.Contains(n, StringComparison.Ordinal)))
            .ToArray();
        Assert.Equal(["Alice", "Bob", "Carol", "Dave"], names);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dtk test tests/RustPlusBot.Features.Players.Tests --filter FullyQualifiedName~ServerTeamMessageRendererTests`
Expected: FAIL — `ServerTeamMessageRenderer` does not exist.

- [ ] **Step 4: Write the renderer**

Create `src/RustPlusBot.Features.Players/Messages/ServerTeamMessageRenderer.cs`:

```csharp
using System.Globalization;
using System.Text;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Players.Messages;

/// <summary>
///     Renders the #info team embed: one line per member with presence, grid reference and how long
///     they have held their current state. Replaces the /team, /online, /offline and /alive commands.
///     Rust caps a team at eight members, so a description list never risks truncation.
/// </summary>
/// <param name="query">Live team query.</param>
/// <param name="afk">Live AFK state.</param>
/// <param name="mapSettings">Supplies the per-server grid convention.</param>
/// <param name="clock">Supplies the current time for durations.</param>
/// <param name="localizer">String resolution.</param>
public sealed class ServerTeamMessageRenderer(
    IRustServerQuery query,
    IAfkState afk,
    IMapSettingsStore mapSettings,
    IClock clock,
    ILocalizer localizer) : IMessageRenderer
{
    /// <summary>The message key this renderer produces.</summary>
    public const string Key = "server.team";

    /// <inheritdoc />
    public string MessageKey => Key;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return new MessagePayload(null, null, null);
        }

        var culture = context.Culture;
        var team = await query.GetTeamInfoAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            // Honest over stale: an empty payload would make the reconciler skip the edit and leave
            // the previous roster on screen as though it were current.
            var offline = new EmbedBuilder()
                .WithTitle(localizer.Get("server.team.title", culture, "0", "0"))
                .WithDescription(localizer.Get("server.team.disconnected", culture))
                .WithColor(Color.Red)
                .Build();
            return new MessagePayload(null, offline, null);
        }

        var online = team.Members.Count(m => m.IsOnline);
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("server.team.title", culture,
                online.ToString(CultureInfo.InvariantCulture),
                team.Members.Count.ToString(CultureInfo.InvariantCulture)))
            .WithColor(Color.Blue);

        if (team.Members.Count == 0)
        {
            return new MessagePayload(null,
                embed.WithDescription(localizer.Get("server.team.empty", culture)).Build(), null);
        }

        var afkIds = await ResolveAfkAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var settings = await mapSettings.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var dims = await query.GetMapDimensionsAsync(context.GuildId, serverId, cancellationToken)
            .ConfigureAwait(false);

        var body = new StringBuilder();
        foreach (var member in team.Members.OrderBy(m => RankOf(m, afkIds)).ThenByDescending(SurvivedFor))
        {
            body.Append(LineFor(member, team.LeaderSteamId, afkIds, dims, settings.GridStyle, culture)).Append('\n');
        }

        return new MessagePayload(null, embed.WithDescription(body.ToString().TrimEnd('\n')).Build(), null);
    }

    private async ValueTask<Dictionary<ulong, TimeSpan>> ResolveAfkAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        var members = await afk.GetAfkMembersAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        return members is null
            ? []
            : members.ToDictionary(m => m.SteamId, m => m.StillFor);
    }

    /// <summary>Sort bucket: online-alive, then AFK, then dead, then offline.</summary>
    private static int RankOf(TeamMemberSnapshot member, Dictionary<ulong, TimeSpan> afkIds)
    {
        if (!member.IsOnline)
        {
            return 3;
        }

        if (!member.IsAlive)
        {
            return 2;
        }

        return afkIds.ContainsKey(member.SteamId) ? 1 : 0;
    }

    private TimeSpan SurvivedFor(TeamMemberSnapshot member) => clock.UtcNow - member.LastSpawnTimeUtc;

    private string LineFor(
        TeamMemberSnapshot member,
        ulong leaderSteamId,
        Dictionary<ulong, TimeSpan> afkIds,
        MapDimensions? dims,
        MapGridStyle style,
        string culture)
    {
        var crown = member.SteamId == leaderSteamId ? "👑" : string.Empty;

        // The API can report a member with no display name; fall back to the id rather than a blank.
        var name = string.IsNullOrWhiteSpace(member.Name)
            ? member.SteamId.ToString(CultureInfo.InvariantCulture)
            : member.Name;

        // Offline members report their last-known position, which would read as current.
        var grid = member.IsOnline
            ? GridReference.From(member.X, member.Y, dims, style)
            : localizer.Get("server.team.nogrid", culture);

        var (glyph, state) = StateFor(member, afkIds, culture);

        return localizer.Get("server.team.line", culture, crown, glyph, name, grid, state);
    }

    private (string Glyph, string State) StateFor(
        TeamMemberSnapshot member,
        Dictionary<ulong, TimeSpan> afkIds,
        string culture)
    {
        if (!member.IsOnline)
        {
            // No disconnect timestamp exists on the snapshot, so no duration is reported here.
            return ("⚫", localizer.Get("server.team.offline", culture));
        }

        if (!member.IsAlive)
        {
            return ("💀", localizer.Get("server.team.dead", culture,
                DurationFormat.Compact(clock.UtcNow - member.LastDeathTimeUtc)));
        }

        if (afkIds.TryGetValue(member.SteamId, out var stillFor))
        {
            return ("😴", localizer.Get("server.team.afk", culture, DurationFormat.Compact(stillFor)));
        }

        return ("🟢", localizer.Get("server.team.alive", culture, DurationFormat.Compact(SurvivedFor(member))));
    }
}
```

- [ ] **Step 5: Register the renderer**

In `src/RustPlusBot.Features.Players/PlayerEventServiceCollectionExtensions.cs`, before `return services;`, add:

```csharp
        // Contributes the #info team embed to the Workspace reconciler.
        services.AddScoped<IMessageRenderer, ServerTeamMessageRenderer>();
```

with these usings:

```csharp
using RustPlusBot.Features.Players.Messages;
using RustPlusBot.Features.Workspace.Registry;
```

- [ ] **Step 6: Run the tests**

Run: `dtk test tests/RustPlusBot.Features.Players.Tests --filter FullyQualifiedName~ServerTeamMessageRendererTests`
Expected: PASS — 9 test cases.

Run: `dtk test tests/RustPlusBot.Localization.Tests`
Expected: PASS at count 312.

- [ ] **Step 7: Format and commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "feat: add the #info team embed

One line per member with presence glyph, grid reference and state age,
replacing /team, /online, /offline and /alive. Sorted online-alive, AFK,
dead, offline."
```

---

### Task 6: Declare the message specs

Wire the two new renderers into the reconciler and fix their in-channel order.

**Files:**
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Specs/ServerWorkspaceSpecProviderTests.cs`

**Interfaces:**
- Consumes: `ServerEventsMessageRenderer.Key == "server.events"` (Task 4), `ServerTeamMessageRenderer.Key == "server.team"` (Task 5).
- Produces: `WorkspaceMessageKeys.ServerEvents == "server.events"`, `WorkspaceMessageKeys.ServerTeam == "server.team"`.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Features.Workspace.Tests/Specs/ServerWorkspaceSpecProviderTests.cs`:

```csharp
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Specs;

namespace RustPlusBot.Features.Workspace.Tests.Specs;

public sealed class ServerWorkspaceSpecProviderTests
{
    [Fact]
    public void Info_channel_messages_are_declared_in_render_order()
    {
        var specs = new ServerWorkspaceSpecProvider().GetMessageSpecs()
            .Where(s => s.ChannelKey == "info")
            .Select(s => s.Key)
            .ToArray();

        // Discord orders by creation time, so declaration order is the on-screen order:
        // map image, then status, then events, then team.
        Assert.Equal(["server.info.map", "server.info", "server.events", "server.team"], specs);
    }

    [Fact]
    public void Every_per_server_message_targets_a_declared_channel()
    {
        var provider = new ServerWorkspaceSpecProvider();
        var channels = provider.GetChannelSpecs().Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        Assert.All(provider.GetMessageSpecs(), s => Assert.Contains(s.ChannelKey, channels));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk test tests/RustPlusBot.Features.Workspace.Tests --filter FullyQualifiedName~ServerWorkspaceSpecProviderTests`
Expected: FAIL — the order assertion reports only `["server.info.map", "server.info"]`.

If `ServerWorkspaceSpecProvider` is `internal`, the test resolves it via the existing `InternalsVisibleTo` entry for `RustPlusBot.Features.Workspace.Tests` declared in the csproj — no visibility change needed.

- [ ] **Step 3: Add the message keys**

In `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`, inside `WorkspaceMessageKeys`, after the `ServerInfo` constant, add:

```csharp
    /// <summary>Key for the per-server #info events embed (cargo/heli/chinook/rigs). Rendered by Features.Events.</summary>
    public const string ServerEvents = "server.events";

    /// <summary>Key for the per-server #info team embed (roster + presence). Rendered by Features.Players.</summary>
    public const string ServerTeam = "server.team";
```

- [ ] **Step 4: Declare the specs**

In `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`, replace the `GetMessageSpecs` body:

```csharp
    /// <inheritdoc />
    public IEnumerable<MessageSpec> GetMessageSpecs() =>
    [
        // Declaration order IS the on-screen order (Discord orders messages by creation time), and
        // the reconciler re-posts later messages to repair drift. Keep: map image, status, events, team.
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerInfoMap, WorkspaceChannelKeys.ServerInfo),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerInfo, WorkspaceChannelKeys.ServerInfo),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerEvents, WorkspaceChannelKeys.ServerInfo),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerTeam, WorkspaceChannelKeys.ServerInfo),
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerMap, WorkspaceChannelKeys.ServerMap),
    ];
```

- [ ] **Step 5: Run the tests**

Run: `dtk test tests/RustPlusBot.Features.Workspace.Tests`
Expected: PASS.

Note the reconciler filters specs by `_renderers.ContainsKey(s.Key)` (`WorkspaceReconciler.cs:239`), so a host that composes Workspace without Events or Players simply skips the corresponding embed rather than failing.

- [ ] **Step 6: Format and commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "feat: declare the events and team message specs in #info

Declaration order fixes the on-screen order: map, status, events, team."
```

---

### Task 7: Gated refresh path

A narrow refresh that skips channel provisioning and suppresses no-op edits.

**Files:**
- Create: `src/RustPlusBot.Features.Workspace/Reconciler/IServerInfoRefresher.cs`
- Create: `src/RustPlusBot.Features.Workspace/Reconciler/ServerInfoRefresher.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/ServerInfoRefresherTests.cs`

**Interfaces:**
- Consumes: `IWorkspaceStore.GetMessageAsync/GetCultureAsync`, `IWorkspaceGateway.EditMessageAsync`, `IWorkspaceReconciler.ReconcileServerAsync`, `RenderGate.ShouldSend/Commit/Invalidate`, `RenderCanonicalizer.Canonicalize`, `IMessageRenderer` (Task 2), the three message keys (Task 6).
- Produces: `internal interface IServerInfoRefresher { Task RefreshAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken); }`, implemented by `ServerInfoRefresher`.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/ServerInfoRefresherTests.cs`:

```csharp
using Discord;
using NSubstitute;
using RustPlusBot.Discord.Posting;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class ServerInfoRefresherTests
{
    private const ulong GuildId = 1;
    private const ulong ChannelId = 500;
    private const ulong MessageId = 900;
    private static readonly Guid ServerId = Guid.NewGuid();

    private sealed class StubRenderer(string key, string title) : IMessageRenderer
    {
        public string MessageKey => key;

        public int Calls { get; private set; }

        public string Title { get; set; } = title;

        public ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
        {
            Calls++;
            var embed = new EmbedBuilder().WithTitle(Title).Build();
            return ValueTask.FromResult(new MessagePayload(null, embed, null));
        }
    }

    private static IWorkspaceStore StoreWith(params string[] keys)
    {
        var store = Substitute.For<IWorkspaceStore>();
        store.GetCultureAsync(GuildId, Arg.Any<CancellationToken>()).Returns("en");
        foreach (var key in keys)
        {
            store.GetMessageAsync(GuildId, ServerId, key, Arg.Any<CancellationToken>())
                .Returns(new ProvisionedMessage
                {
                    GuildId = GuildId,
                    RustServerId = ServerId,
                    MessageKey = key,
                    DiscordChannelId = ChannelId,
                    DiscordMessageId = MessageId,
                });
        }

        return store;
    }

    [Fact]
    public async Task First_refresh_edits_the_message()
    {
        var renderer = new StubRenderer(WorkspaceMessageKeys.ServerInfo, "v1");
        var gateway = Substitute.For<IWorkspaceGateway>();
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var refresher = new ServerInfoRefresher(StoreWith(WorkspaceMessageKeys.ServerInfo), gateway, [renderer],
            new RenderGate(), reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);

        await gateway.Received(1).EditMessageAsync(GuildId, ChannelId, MessageId, Arg.Any<MessagePayload>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unchanged_render_is_not_sent_twice()
    {
        var renderer = new StubRenderer(WorkspaceMessageKeys.ServerInfo, "v1");
        var gateway = Substitute.For<IWorkspaceGateway>();
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var refresher = new ServerInfoRefresher(StoreWith(WorkspaceMessageKeys.ServerInfo), gateway, [renderer],
            new RenderGate(), reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);
        await refresher.RefreshAsync(GuildId, ServerId, default);

        Assert.Equal(2, renderer.Calls);
        await gateway.Received(1).EditMessageAsync(GuildId, ChannelId, MessageId, Arg.Any<MessagePayload>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Changed_render_is_sent_again()
    {
        var renderer = new StubRenderer(WorkspaceMessageKeys.ServerInfo, "v1");
        var gateway = Substitute.For<IWorkspaceGateway>();
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var refresher = new ServerInfoRefresher(StoreWith(WorkspaceMessageKeys.ServerInfo), gateway, [renderer],
            new RenderGate(), reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);
        renderer.Title = "v2";
        await refresher.RefreshAsync(GuildId, ServerId, default);

        await gateway.Received(2).EditMessageAsync(GuildId, ChannelId, MessageId, Arg.Any<MessagePayload>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_message_row_escalates_to_full_reconcile()
    {
        var renderer = new StubRenderer(WorkspaceMessageKeys.ServerInfo, "v1");
        var store = Substitute.For<IWorkspaceStore>();
        store.GetCultureAsync(GuildId, Arg.Any<CancellationToken>()).Returns("en");
        store.GetMessageAsync(GuildId, ServerId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ProvisionedMessage?)null);
        var gateway = Substitute.For<IWorkspaceGateway>();
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var refresher = new ServerInfoRefresher(store, gateway, [renderer], new RenderGate(), reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);

        await reconciler.Received(1).ReconcileServerAsync(GuildId, ServerId, Arg.Any<CancellationToken>());
        await gateway.DidNotReceive().EditMessageAsync(Arg.Any<ulong>(), Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<MessagePayload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_failure_escalates_and_invalidates_the_gate()
    {
        var renderer = new StubRenderer(WorkspaceMessageKeys.ServerInfo, "v1");
        var gateway = Substitute.For<IWorkspaceGateway>();
        gateway.EditMessageAsync(GuildId, ChannelId, MessageId, Arg.Any<MessagePayload>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("404")));
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var gate = new RenderGate();
        var refresher = new ServerInfoRefresher(StoreWith(WorkspaceMessageKeys.ServerInfo), gateway, [renderer], gate,
            reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);

        await reconciler.Received(1).ReconcileServerAsync(GuildId, ServerId, Arg.Any<CancellationToken>());
        // The gate must not remember a render that never landed.
        Assert.True(gate.ShouldSend(MessageId, "anything"));
    }

    [Fact]
    public async Task Empty_payload_is_skipped_without_editing()
    {
        var renderer = Substitute.For<IMessageRenderer>();
        renderer.MessageKey.Returns(WorkspaceMessageKeys.ServerInfo);
        renderer.RenderAsync(Arg.Any<MessageRenderContext>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new MessagePayload(null, null, null)));
        var gateway = Substitute.For<IWorkspaceGateway>();
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var refresher = new ServerInfoRefresher(StoreWith(WorkspaceMessageKeys.ServerInfo), gateway, [renderer],
            new RenderGate(), reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);

        await gateway.DidNotReceive().EditMessageAsync(Arg.Any<ulong>(), Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<MessagePayload>(), Arg.Any<CancellationToken>());
        await reconciler.DidNotReceive().ReconcileServerAsync(Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refreshes_all_three_info_messages()
    {
        var renderers = new IMessageRenderer[]
        {
            new StubRenderer(WorkspaceMessageKeys.ServerInfo, "a"),
            new StubRenderer(WorkspaceMessageKeys.ServerEvents, "b"),
            new StubRenderer(WorkspaceMessageKeys.ServerTeam, "c"),
        };
        var store = StoreWith(WorkspaceMessageKeys.ServerInfo, WorkspaceMessageKeys.ServerEvents,
            WorkspaceMessageKeys.ServerTeam);
        var gateway = Substitute.For<IWorkspaceGateway>();
        var refresher = new ServerInfoRefresher(store, gateway, renderers, new RenderGate(),
            Substitute.For<IWorkspaceReconciler>());

        await refresher.RefreshAsync(GuildId, ServerId, default);

        Assert.All(renderers.Cast<StubRenderer>(), r => Assert.Equal(1, r.Calls));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dtk test tests/RustPlusBot.Features.Workspace.Tests --filter FullyQualifiedName~ServerInfoRefresherTests`
Expected: FAIL — `ServerInfoRefresher` does not exist.

- [ ] **Step 3: Write the interface**

Create `src/RustPlusBot.Features.Workspace/Reconciler/IServerInfoRefresher.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>
///     Re-renders a server's #info messages in place without re-walking channel provisioning.
///     The full <see cref="IWorkspaceReconciler.ReconcileServerAsync" /> takes the per-guild
///     provisioning lock and re-checks every category and channel — far too heavy for a
///     minute-by-minute pulse. This path reads the message ids straight from the store and edits.
/// </summary>
internal interface IServerInfoRefresher
{
    /// <summary>Re-renders the server's #info messages, editing only those whose render changed.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when every message has been considered.</returns>
    Task RefreshAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Write the implementation**

Create `src/RustPlusBot.Features.Workspace/Reconciler/ServerInfoRefresher.cs`:

```csharp
using RustPlusBot.Discord.Posting;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Render-gated in-place refresh of the three #info messages.</summary>
/// <param name="store">Supplies the provisioned message ids and the guild culture.</param>
/// <param name="gateway">Performs the Discord edit.</param>
/// <param name="renderers">All registered message renderers.</param>
/// <param name="gate">Suppresses PATCHes for renders that did not change.</param>
/// <param name="reconciler">The full reconcile, used to self-heal a missing or stale message.</param>
internal sealed class ServerInfoRefresher(
    IWorkspaceStore store,
    IWorkspaceGateway gateway,
    IEnumerable<IMessageRenderer> renderers,
    RenderGate gate,
    IWorkspaceReconciler reconciler) : IServerInfoRefresher
{
    /// <summary>The #info message keys this path refreshes, in declaration order.</summary>
    private static readonly string[] Keys =
    [
        WorkspaceMessageKeys.ServerInfo,
        WorkspaceMessageKeys.ServerEvents,
        WorkspaceMessageKeys.ServerTeam,
    ];

    private readonly Dictionary<string, IMessageRenderer> _renderers =
        renderers.ToDictionary(r => r.MessageKey, StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task RefreshAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var culture = await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        var context = new MessageRenderContext(guildId, serverId, culture);

        foreach (var key in Keys)
        {
            if (!_renderers.TryGetValue(key, out var renderer))
            {
                // The host did not compose the feature that owns this embed; nothing to refresh.
                continue;
            }

            var payload = await renderer.RenderAsync(context, cancellationToken).ConfigureAwait(false);
            if (payload.Text is null && payload.Embed is null && payload.Components is null)
            {
                // Nothing to show (e.g. the server row vanished mid-tick). Leave the message alone.
                continue;
            }

            var record = await store.GetMessageAsync(guildId, serverId, key, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                // Never posted (or wiped): the full reconcile owns creating it.
                await reconciler.ReconcileServerAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
                return;
            }

            var canonical = RenderCanonicalizer.Canonicalize(payload.Embed, payload.Components);
            if (!gate.ShouldSend(record.DiscordMessageId, canonical))
            {
                continue;
            }

            try
            {
                await gateway
                    .EditMessageAsync(guildId, record.DiscordChannelId, record.DiscordMessageId, payload,
                        cancellationToken)
                    .ConfigureAwait(false);
                gate.Commit(record.DiscordMessageId, canonical);
            }
#pragma warning disable CA1031 // Broad catch: any edit failure (deleted message, permissions, 5xx) heals the same way.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Never remember a render that did not land, or the next tick would skip it too.
                gate.Invalidate(record.DiscordMessageId);
                await reconciler.ReconcileServerAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
    }
}
```

- [ ] **Step 5: Register it**

In `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`, immediately after the `IWorkspaceReconciler` registration, add:

```csharp
        services.AddScoped<IServerInfoRefresher, ServerInfoRefresher>();
```

`RenderGate` is already a DI singleton registered by the Discord layer. If `dtk build` reports it cannot be resolved from the Workspace container, add `services.TryAddSingleton<RenderGate>();` alongside, with `using Microsoft.Extensions.DependencyInjection.Extensions;`.

- [ ] **Step 6: Run the tests**

Run: `dtk test tests/RustPlusBot.Features.Workspace.Tests --filter FullyQualifiedName~ServerInfoRefresherTests`
Expected: PASS — 7 test cases.

- [ ] **Step 7: Format and commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "feat: add a render-gated in-place refresh for #info

Skips the provisioning lock and channel walk that ReconcileServerAsync
performs, and suppresses PATCHes for unchanged renders. Any edit failure
invalidates the gate and escalates to a full reconcile."
```

---

### Task 8: Refresh hosted service

**Files:**
- Create: `src/RustPlusBot.Features.Workspace/Hosting/ServerInfoRefreshHostedService.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceOptions.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Host/appsettings.json`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Hosting/ServerInfoRefreshHostedServiceTests.cs`

**Interfaces:**
- Consumes: `IServerInfoRefresher.RefreshAsync` (Task 7); `IEventBus.SubscribeAsync<ConnectionStatusChangedEvent>`; `ConnectionStatusChangedEvent` with its `GuildId`, `ServerId`, `IsConnected` members.
- Produces: `WorkspaceOptions.InfoRefreshInterval` (`TimeSpan`, default 1 minute).

- [ ] **Step 1: Confirm the event shape**

Run: `cat src/RustPlusBot.Abstractions/Events/ConnectionStatusChangedEvent.cs`

Note the exact property names. The implementation below assumes `GuildId`, `ServerId` and `IsConnected`; adjust the two references in Step 4 if they differ.

- [ ] **Step 2: Add the option**

Replace `src/RustPlusBot.Features.Workspace/WorkspaceOptions.cs`:

```csharp
namespace RustPlusBot.Features.Workspace;

/// <summary>Workspace feature configuration, bound from the "Workspace" config section.</summary>
public sealed class WorkspaceOptions
{
    /// <summary>Enables dangerous developer commands (/workspace reset, /workspace simulate-server).</summary>
    public bool EnableDangerCommands { get; set; }

    /// <summary>
    ///     How often every connected server's #info embeds are re-rendered. Unchanged renders are
    ///     suppressed by the render gate, so the steady-state cost is three Rust+ calls per server
    ///     per tick and at most three Discord edits.
    /// </summary>
    public TimeSpan InfoRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
}
```

And in `src/RustPlusBot.Host/appsettings.json`, change the `Workspace` section to:

```json
  "Workspace": {
    "EnableDangerCommands": false,
    "InfoRefreshInterval": "00:01:00"
  },
```

- [ ] **Step 3: Write the failing test**

Create `tests/RustPlusBot.Features.Workspace.Tests/Hosting/ServerInfoRefreshHostedServiceTests.cs`:

```csharp
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Features.Workspace.Hosting;

namespace RustPlusBot.Features.Workspace.Tests.Hosting;

public sealed class ServerInfoRefreshHostedServiceTests
{
    [Fact]
    public void Interval_is_clamped_to_a_one_second_floor()
    {
        var options = Options.Create(new WorkspaceOptions
        {
            InfoRefreshInterval = TimeSpan.Zero
        });

        Assert.Equal(TimeSpan.FromSeconds(1), ServerInfoRefreshHostedService.ResolveInterval(options.Value));
    }

    [Fact]
    public void Interval_is_clamped_when_negative()
    {
        var options = Options.Create(new WorkspaceOptions
        {
            InfoRefreshInterval = TimeSpan.FromSeconds(-30)
        });

        Assert.Equal(TimeSpan.FromSeconds(1), ServerInfoRefreshHostedService.ResolveInterval(options.Value));
    }

    [Fact]
    public void Configured_interval_passes_through()
    {
        var options = Options.Create(new WorkspaceOptions
        {
            InfoRefreshInterval = TimeSpan.FromMinutes(5)
        });

        Assert.Equal(TimeSpan.FromMinutes(5), ServerInfoRefreshHostedService.ResolveInterval(options.Value));
    }

    [Fact]
    public void Connected_servers_are_tracked_and_dropped_on_disconnect()
    {
        var tracker = new ConnectedServerSet();
        var serverId = Guid.NewGuid();

        tracker.Set(1, serverId, connected: true);
        Assert.Contains((1UL, serverId), tracker.Snapshot());

        tracker.Set(1, serverId, connected: false);
        Assert.DoesNotContain((1UL, serverId), tracker.Snapshot());
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dtk test tests/RustPlusBot.Features.Workspace.Tests --filter FullyQualifiedName~ServerInfoRefreshHostedServiceTests`
Expected: FAIL — neither `ServerInfoRefreshHostedService` nor `ConnectedServerSet` exists.

- [ ] **Step 5: Write the hosted service**

Create `src/RustPlusBot.Features.Workspace/Hosting/ServerInfoRefreshHostedService.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Reconciler;

namespace RustPlusBot.Features.Workspace.Hosting;

/// <summary>The set of servers currently believed connected, maintained from connection status events.</summary>
internal sealed class ConnectedServerSet
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), byte> _connected = new();

    /// <summary>Adds or removes a server from the refresh rotation.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="connected">True to track the server; false to drop it.</param>
    public void Set(ulong guildId, Guid serverId, bool connected)
    {
        if (connected)
        {
            _connected[(guildId, serverId)] = 0;
        }
        else
        {
            _connected.TryRemove((guildId, serverId), out _);
        }
    }

    /// <summary>A point-in-time copy of the tracked servers.</summary>
    /// <returns>The currently-tracked (guild, server) pairs.</returns>
    public IReadOnlyList<(ulong Guild, Guid Server)> Snapshot() => [.. _connected.Keys];
}

/// <summary>
///     Re-renders every connected server's #info embeds on a steady interval. In-game time, population
///     and team state change continuously, and the workspace reconciler is purely event-driven — without
///     this tick the embeds would only update on connect, disconnect, wipe or credential change.
/// </summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="options">Supplies the refresh interval.</param>
/// <param name="scopeFactory">Opens a scope per refresh (the refresher is scoped).</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ServerInfoRefreshHostedService(
    IEventBus eventBus,
    IOptions<WorkspaceOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<ServerInfoRefreshHostedService> logger) : IHostedService, IDisposable
{
    private readonly ConnectedServerSet _connected = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _statusLoop;
    private Task? _tickLoop;

    /// <summary>Resolves the effective interval, flooring it so a misconfiguration cannot spin the loop.</summary>
    /// <param name="options">The bound workspace options.</param>
    /// <returns>The interval to sleep between ticks.</returns>
    public static TimeSpan ResolveInterval(WorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var floor = TimeSpan.FromSeconds(1);
        return options.InfoRefreshInterval < floor ? floor : options.InfoRefreshInterval;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _statusLoop = Task.Run(() => ConsumeStatusEventsAsync(_cts.Token), CancellationToken.None);
        _tickLoop = Task.Run(() => RunPeriodicRefreshAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[] { _statusLoop, _tickLoop })
        {
            if (loop is null)
            {
                continue;
            }

            try
            {
                await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    private async Task ConsumeStatusEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                _connected.Set(evt.GuildId, evt.ServerId, evt.IsConnected);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogStatusLoopFaulted(logger, ex);
        }
    }

    private async Task RunPeriodicRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(ResolveInterval(options.Value), cancellationToken).ConfigureAwait(false);
                foreach (var (guildId, serverId) in _connected.Snapshot())
                {
                    await RefreshAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting tick must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogTickLoopFaulted(logger, ex);
        }
    }

    private async Task RefreshAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var refresher = scope.ServiceProvider.GetRequiredService<IServerInfoRefresher>();
                await refresher.RefreshAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Broad catch: one server's failure must not stop the rotation.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRefreshFaulted(logger, ex, serverId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "#info status loop faulted.")]
    private static partial void LogStatusLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "#info refresh tick faulted.")]
    private static partial void LogTickLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "#info refresh failed for server {ServerId}.")]
    private static partial void LogRefreshFaulted(ILogger logger, Exception exception, Guid serverId);
}
```

- [ ] **Step 6: Register the hosted service**

In `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`, directly after the existing `services.AddHostedService<Hosting.WorkspaceHostedService>();`, add:

```csharp
        services.AddHostedService<Hosting.ServerInfoRefreshHostedService>();
```

- [ ] **Step 7: Run the tests**

Run: `dtk test tests/RustPlusBot.Features.Workspace.Tests`
Expected: PASS.

Run: `dtk build RustPlusBot.slnx`
Expected: PASS.

- [ ] **Step 8: Format and commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "feat: refresh #info on a configurable interval

Tracks connected servers off the connection status event and re-renders
their #info embeds every Workspace:InfoRefreshInterval (default 1m),
floored at one second so a misconfiguration cannot spin the loop."
```

---

### Task 9: Delete the slash commands, move the resolver

**Files:**
- Delete: `src/RustPlusBot.Features.Commands/Modules/ServerCommandModule.cs`
- Delete: `src/RustPlusBot.Features.Commands/Servers/ServerQueryService.cs`
- Delete: `tests/RustPlusBot.Features.Commands.Tests/Servers/ServerQueryServiceTests.cs`
- Move: `src/RustPlusBot.Features.Commands/Servers/{ServerResolver,ServerResolution,ServerAutocompleteHandler}.cs` → `src/RustPlusBot.Features.Connections/Servers/`
- Move: `tests/RustPlusBot.Features.Commands.Tests/Servers/ServerResolverTests.cs` → `tests/RustPlusBot.Features.Connections.Tests/Servers/`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs`
- Modify: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `RustPlusBot.Features.Connections.Servers.ServerResolver`, `.ServerResolution(Guid? ServerId, string? Name, string? ErrorMessage)`, `.ServerAutocompleteHandler`. `ServerResolver` stays `internal`; `ServerAutocompleteHandler` stays `public` (Discord.Net instantiates it reflectively).

- [ ] **Step 1: Move the three files**

```bash
mkdir -p src/RustPlusBot.Features.Connections/Servers
git mv src/RustPlusBot.Features.Commands/Servers/ServerResolver.cs src/RustPlusBot.Features.Connections/Servers/
git mv src/RustPlusBot.Features.Commands/Servers/ServerResolution.cs src/RustPlusBot.Features.Connections/Servers/
git mv src/RustPlusBot.Features.Commands/Servers/ServerAutocompleteHandler.cs src/RustPlusBot.Features.Connections/Servers/
```

In all three files, change:

```csharp
namespace RustPlusBot.Features.Commands.Servers;
```

to:

```csharp
namespace RustPlusBot.Features.Connections.Servers;
```

- [ ] **Step 2: Delete the dead code**

```bash
git rm src/RustPlusBot.Features.Commands/Modules/ServerCommandModule.cs
git rm src/RustPlusBot.Features.Commands/Servers/ServerQueryService.cs
git rm tests/RustPlusBot.Features.Commands.Tests/Servers/ServerQueryServiceTests.cs
rmdir src/RustPlusBot.Features.Commands/Servers 2>/dev/null || true
```

- [ ] **Step 3: Move the registration**

In `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`, delete these two lines:

```csharp
        services.AddScoped<ServerResolver>();
        services.AddScoped<ServerQueryService>();
```

and remove the now-unused `using RustPlusBot.Features.Commands.Servers;` if the file has no other reference to that namespace.

In `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs`, add before the `InteractionModuleAssembly` registration:

```csharp
        services.AddScoped<ServerResolver>();
```

with `using RustPlusBot.Features.Connections.Servers;` at the top.

- [ ] **Step 4: Move the resolver test**

```bash
mkdir -p tests/RustPlusBot.Features.Connections.Tests/Servers
git mv tests/RustPlusBot.Features.Commands.Tests/Servers/ServerResolverTests.cs tests/RustPlusBot.Features.Connections.Tests/Servers/
```

In the moved file, change the namespace to `RustPlusBot.Features.Connections.Tests.Servers` and the `using RustPlusBot.Features.Commands.Servers;` to `using RustPlusBot.Features.Connections.Servers;`.

If `tests/RustPlusBot.Features.Connections.Tests` lacks an `InternalsVisibleTo` grant for the internal `ServerResolver`, add to `src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj` (mirroring the existing `InternalsVisibleTo` item you saw in the csproj):

```xml
    <InternalsVisibleTo Include="RustPlusBot.Features.Connections.Tests" />
```

- [ ] **Step 5: Fix CommandRegistrationTests**

In `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`, delete these two assertions from `Dispatcher_and_handlers_resolve`:

```csharp
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ServerResolver>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ServerQueryService>());
```

and remove `using RustPlusBot.Features.Commands.Servers;`.

Leave `Assert.Equal(28, handlers.Count);` **unchanged** — no `ICommandHandler` is removed by this task, which is exactly the guard that the in-game surface survived.

- [ ] **Step 6: Add the mirrored Connections assertion**

Add to `tests/RustPlusBot.Features.Connections.Tests/` a registration test (extend the existing one if present, otherwise create `ConnectionRegistrationTests.cs`):

```csharp
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ServerResolver>());
```

- [ ] **Step 7: Run the tests**

Run: `dtk build RustPlusBot.slnx`
Expected: PASS — no dangling references to the deleted types.

Run: `dtk test tests/RustPlusBot.Features.Commands.Tests`
Expected: PASS — including `QueryHandlersTests` **unmodified**, proving every `!command` still works.

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests`
Expected: PASS — including the moved `ServerResolverTests`.

- [ ] **Step 8: Format and commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "refactor: delete the nine server-data slash commands

Their data now lives in the #info embeds. The in-game !pop/!time/!wipe/
!online/!offline/!team/!alive/!small/!large handlers are untouched.
ServerResolver moves to Features.Connections, which /server player needs
because Features.Commands cannot see WorkspaceComponentIds."
```

---

### Task 10: `/server player`

**Files:**
- Create: `src/RustPlusBot.Features.Connections/Modules/ServerPlayerModule.cs`
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `Strings.fr.resx`
- Modify: `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs:44`

**Interfaces:**
- Consumes: `ServerResolver.ResolveAsync(ulong, string?, string, CancellationToken) -> ServerResolution` and `ServerAutocompleteHandler` (Task 9); `WorkspaceComponentIds.ServerInfoSwapPrefix`; `IConnectionStore.ListPoolAsync`, `GetStateAsync`; the retained `server.info.swap.placeholder` key.
- Produces: the `/server player` slash command. No new types are consumed downstream.

- [ ] **Step 1: Add the resx keys**

Add to `Strings.resx`:

| Key | Value |
| --- | ----- |
| `command.server.player.pick` | `Choose which paired player drives the connection.` |
| `command.server.player.nopool` | `No paired players are available for that server.` |
| `help.slash.server.player` | `Switch which paired player drives a server's connection` |

And `Strings.fr.resx`:

| Key | Value |
| --- | ----- |
| `command.server.player.pick` | `Choisissez quel joueur associé pilote la connexion.` |
| `command.server.player.nopool` | `Aucun joueur associé n'est disponible pour ce serveur.` |
| `help.slash.server.player` | `Changer le joueur associé qui pilote la connexion d'un serveur` |

That is **3 new keys**: update the count assertion from `312` to `315`.

- [ ] **Step 2: Write the module**

Create `src/RustPlusBot.Features.Connections/Modules/ServerPlayerModule.cs`:

```csharp
using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Connections.Servers;
using RustPlusBot.Features.Workspace;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Connections.Modules;

/// <summary>
///     /server player — replaces the "switch active player" select that used to sit on the #info
///     embed. It replies ephemerally with that same select (same custom id), so
///     <see cref="ConnectionComponentModule" /> handles the choice with no new interaction logic.
/// </summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
[Group("server", "Server administration")]
public sealed class ServerPlayerModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Shows the credential picker for a server.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    /// <returns>A task that completes when the reply has been sent.</returns>
    [SlashCommand("player", "Switch which paired player drives a server's connection")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task PlayerAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var resolver = scope.ServiceProvider.GetRequiredService<ServerResolver>();
            var localizer = scope.ServiceProvider.GetRequiredService<ILocalizer>();
            var connections = scope.ServiceProvider.GetRequiredService<IConnectionStore>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
            var resolution = await resolver
                .ResolveAsync(Context.Guild.Id, server, culture, CancellationToken.None).ConfigureAwait(false);
            if (resolution.ErrorMessage is { } error)
            {
                await FollowupAsync(error, ephemeral: true).ConfigureAwait(false);
                return;
            }

            var serverId = resolution.ServerId!.Value;
            var state = await connections.GetStateAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            var pool = await connections.ListPoolAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            var eligible = pool
                .Where(c => c.Status != CredentialStatus.Invalid)
                .Take(SelectMenuBuilder.MaxOptionCount) // Discord hard-limits a select menu to 25 options.
                .ToList();
            if (eligible.Count == 0)
            {
                await FollowupAsync(localizer.Get("command.server.player.nopool", culture), ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            var select = new SelectMenuBuilder()
                .WithCustomId($"{WorkspaceComponentIds.ServerInfoSwapPrefix}{serverId}")
                .WithPlaceholder(localizer.Get("server.info.swap.placeholder", culture));
            foreach (var credential in eligible)
            {
                select.AddOption(
                    credential.SteamId.ToString(CultureInfo.InvariantCulture),
                    credential.Id.ToString(),
                    isDefault: credential.Id == state?.ActiveCredentialId);
            }

            await FollowupAsync(
                    localizer.Get("command.server.player.pick", culture),
                    components: new ComponentBuilder().WithSelectMenu(select).Build(),
                    ephemeral: true)
                .ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 3: List it in /help**

In `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`, add to the `Slash` list immediately after the `leader` entry:

```csharp
        new("server player", CommandGroup.Bot, "help.slash.server.player"),
```

- [ ] **Step 4: Build and run the suite**

Run: `dtk build RustPlusBot.slnx`
Expected: PASS.

Run: `dtk test RustPlusBot.slnx`
Expected: PASS — the whole suite.

If a help-catalog drift guard asserts every `Slash` entry maps to a registered slash command, it may need the `"server player"` name spelled as the framework reports it (Discord groups render as `server player`). Adjust to match the guard's expectation rather than weakening the guard.

- [ ] **Step 5: Format and commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "feat: add /server player to replace the #info swap select

Replies ephemerally with the same select and custom id the embed used to
carry, so ConnectionComponentModule handles it unchanged."
```

---

## Final verification

- [ ] **Full build and suite**

Run: `dtk build RustPlusBot.slnx && dtk test RustPlusBot.slnx`
Expected: PASS, with a test count higher than the pre-branch baseline by roughly 45 (13 Daylight + 3 server embed + 8 events + 9 team + 2 spec + 7 refresher + 4 hosted service, less the 3 deleted `ServerInfo` renderer tests and the deleted `ServerQueryServiceTests`).

- [ ] **Format gate**

Run: `dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx && git diff --exit-code`
Expected: exit 0, no diff. This is the hard CI gate.

- [ ] **Live smoke (user gate — do not mark complete without the user confirming)**

1. Start the bot against a live paired server.
2. Confirm `#info` shows four messages in order: map image, server status, events, team.
3. Wait one interval; confirm the server embed's in-game time advances.
4. Confirm the nine slash commands no longer appear in Discord's picker.
5. In-game, run `!pop`, `!time`, `!wipe`, `!online`, `!offline`, `!team`, `!alive`, `!small`, `!large` — all must still answer.
6. Run `/server player` and confirm it swaps the active credential.
7. Check the log for repeated `#info refresh failed` warnings — there should be none.
