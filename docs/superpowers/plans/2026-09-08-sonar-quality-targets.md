# SonarQube Quality Targets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take the RustPlusBot `develop` SonarQube analysis to 0 code smells, 0% duplicated lines, and above 90% coverage without changing observable behaviour.

**Architecture:** Three shared seams absorb the duplication — an `EventLoopHostedService` base in `RustPlusBot.Abstractions`, a new `RustPlusBot.Features.Devices` project holding the generic smart-device pairing/posting/relay scaffolding, and a `PairedDeviceStore<TEntity>` base in `RustPlusBot.Persistence`. The four high-complexity methods split into named private steps, which both clears S3776 and creates directly testable surface. Coverage then comes from real tests, with a narrow exclusion list for five I/O adapters that have no injectable seam.

**Tech Stack:** .NET 10 (SDK 10.0.400), C# with nullable + implicit usings, xUnit 2.9.3, NSubstitute 6.2.0, coverlet.collector 10.0.1, Discord.Net 3.20.1, EF Core 10 (SQLite), SonarAnalyzer.CSharp 10.33 running in-build.

**Spec:** `docs/superpowers/specs/2026-09-08-sonar-quality-targets-design.md`

## Global Constraints

- `TreatWarningsAsErrors` is `true` with `AnalysisLevel=latest-all`. NetAnalyzers, Roslynator, Roslynator.Formatting and VS Threading run during build and any new warning fails it. **Run `dotnet build` after every change.**
- **Correction (2026-09-08): `dotnet build` does NOT enforce the SonarQube smell rules.** Verified empirically: a deliberately over-complex method compiled with zero `S3776` diagnostics, and the probe file was genuinely analysed (adding a syntax error to it failed the build with CS1514). `.editorconfig` sets severities for only three `S*` rules (S2094, S4581, S3220). Treat a clean build as a **regression check only** — it is NOT evidence that S3776, S107 or S1192 is cleared. The only binding evidence is a SonarQube analysis (Task 21, Step 6). The MCP `analyze_code_snippet` tool cannot substitute: it has no C# language support.
- `GenerateDocumentationFile` is `true`. Every new public and internal type and member needs XML doc comments, including `<param>` for each parameter and `<returns>` where applicable. Missing docs fail the build.
- Central package management: add package versions in `Directory.Packages.props`, reference without a version in the `.csproj`.
- New `internal` types that need testing require `<InternalsVisibleTo Include="<TestProject>" />` in the owning `.csproj`. Follow the existing pattern (see `src/RustPlusBot.Features.Switches/RustPlusBot.Features.Switches.csproj`).
- Broad `catch (Exception)` needs `#pragma warning disable CA1031` with an inline justification comment; awaiting a stored loop `Task` needs `#pragma warning disable VSTHRD003`. Both patterns already exist in the codebase — copy their shape.
- Test projects use `<Using Include="Xunit" />` and reference only the one production project under test.
- New projects must be added to `RustPlusBot.slnx`.
- Commit after each task. Message style follows the repo: `fix:`, `feat:`, `test:`, `refactor:`, `chore:`, `docs:`. End every commit message with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- Do not touch `**/Migrations/*.cs`.

## Verification commands

```bash
# Fast inner loop for one project
dtk dotnet test tests/<Project>.Tests/<Project>.Tests.csproj

# Whole suite
dtk dotnet test

# Coverage (what the final measurement uses)
dtk dotnet test --collect:"XPlat Code Coverage;Format=opencover"
```

Coverage reports land in `tests/*/TestResults/<guid>/coverage.opencover.xml`. Task 22 aggregates them.

---

## Phase A — Code smells

### Task 1: Collapse `MarkerReply.ForAsync` parameter list (S107)

**Files:**
- Modify: `src/RustPlusBot.Features.Commands/Handlers/MarkerReply.cs:25-33`
- Modify: callers — `ChinookCommandHandler.cs`, and the cargo and heli handlers in `src/RustPlusBot.Features.Commands/Handlers/`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/` (existing marker-reply tests)

**Interfaces:**
- Produces: `internal sealed record MarkerReplyServices(IEventState State, ILocalizer Localizer, IClock Clock, IMapSettingsStore MapSettings)` in `src/RustPlusBot.Features.Commands/Handlers/MarkerReplyServices.cs`, and `MarkerReply.ForAsync(MarkerReplyServices services, CommandContext context, MarkerKind kind, string prefix, CancellationToken cancellationToken)` — 5 parameters.

- [ ] **Step 1: Find every caller**

```bash
grep -rn "MarkerReply.ForAsync" src tests
```

- [ ] **Step 2: Run the existing tests and record the baseline**

Run: `dtk dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj`
Expected: PASS. These tests are the behaviour guard for this refactor — they must still pass unchanged at the end.

- [ ] **Step 3: Add the services record**

Create `src/RustPlusBot.Features.Commands/Handlers/MarkerReplyServices.cs`:

```csharp
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>The collaborators <see cref="MarkerReply"/> needs to format a marker reply.</summary>
/// <param name="State">The live event state reader.</param>
/// <param name="Localizer">The reply localizer.</param>
/// <param name="Clock">Supplies "how long ago" for the suffix.</param>
/// <param name="MapSettings">Supplies the server's grid style for the reference.</param>
internal sealed record MarkerReplyServices(
    IEventState State,
    ILocalizer Localizer,
    IClock Clock,
    IMapSettingsStore MapSettings);
```

- [ ] **Step 4: Change the signature and body**

In `MarkerReply.cs`, replace the four service parameters with `MarkerReplyServices services` as the first parameter, keeping `context`, `kind`, `prefix` and `cancellationToken`. Inside the body, replace `state.` with `services.State.`, `localizer.` with `services.Localizer.`, `clock.` with `services.Clock.` and `mapSettings.` with `services.MapSettings.`. Update the XML doc so it has exactly one `<param>` per remaining parameter.

- [ ] **Step 5: Update every caller found in Step 1**

Each caller constructs `new MarkerReplyServices(state, localizer, clock, mapSettings)` inline at the call site.

- [ ] **Step 6: Build and test**

Run: `dotnet build && dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj`
Expected: build clean (no S107, no missing-doc warnings), tests PASS with no test edits.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "refactor: group MarkerReply collaborators into a services record

Clears sonar S107 (8 parameters) on MarkerReply.ForAsync.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Collapse `MapRenderer.Render` parameter list (S107)

**Files:**
- Modify: `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs:50-58`
- Create: `src/RustPlusBot.Features.Map/Rendering/MapRenderRequest.cs`
- Modify: callers (find with grep)
- Test: `tests/RustPlusBot.Features.Map.Tests/`

**Interfaces:**
- Produces: `internal sealed record MapRenderRequest` with init-only members `byte[] BaseJpeg`, `MapProjection Projection`, `IReadOnlyList<MarkerPlacement> Markers`, `IReadOnlyList<MonumentPlacement> Monuments`, `IReadOnlyList<PlayerPlacement> Players`, `IReadOnlyList<RigPlacement> Rigs`, `MapLayerSet Layers`, `MapGridStyle GridStyle` (default `MapGridStyle.InGame`), `IReadOnlyList<MonumentPlacement>? Tunnels` (default `null`); and `MapRenderer.Render(MapRenderRequest request)`.

- [ ] **Step 1: Find every caller**

```bash
grep -rn "\.Render(" src/RustPlusBot.Features.Map tests/RustPlusBot.Features.Map.Tests | grep -v RenderPrompt
```

- [ ] **Step 2: Run the map tests for a baseline**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests/RustPlusBot.Features.Map.Tests.csproj`
Expected: PASS.

- [ ] **Step 3: Add the request record**

Create `MapRenderRequest.cs` with the members listed under Interfaces. Give the record a `<summary>` and one `<param>`-equivalent `<summary>` per member (use property-level `/// <summary>` since these are init-only properties, not positional parameters, so that defaults can be expressed). Keep the wording of the existing `<param>` docs on `Render` — move them onto the corresponding properties verbatim.

- [ ] **Step 4: Change `Render` to take the request**

Signature becomes `public byte[] Render(MapRenderRequest request)`. First line of the body: `ArgumentNullException.ThrowIfNull(request);`. Keep every existing `ArgumentNullException.ThrowIfNull` guard, retargeted at `request.BaseJpeg`, `request.Projection`, `request.Markers`, `request.Monuments`, `request.Players`, `request.Rigs`, `request.Layers` — these guards are asserted by existing tests, so removing any of them will break them.

- [ ] **Step 5: Update callers and tests**

Test call sites become object-initialiser construction. This is the one task where test edits are expected — they are mechanical call-shape changes, not assertion changes. Do not change a single assertion.

- [ ] **Step 6: Build and test**

Run: `dotnet build && dotnet test tests/RustPlusBot.Features.Map.Tests/RustPlusBot.Features.Map.Tests.csproj`
Expected: build clean, tests PASS.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "refactor: take a MapRenderRequest instead of nine parameters

Clears sonar S107 on MapRenderer.Render.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Collapse the generator's 8-parameter method (S107)

**Files:**
- Modify: `tools/RustPlusBot.ItemData.Generator/Program.cs:140-148`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/`

- [ ] **Step 1: Read the method and its callers**

```bash
sed -n '120,197p' tools/RustPlusBot.ItemData.Generator/Program.cs
grep -rn "<method name from step 1>" tools tests
```

- [ ] **Step 2: Baseline the generator tests**

Run: `dtk dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: PASS.

- [ ] **Step 3: Introduce a parameter record**

Group the parameters that travel together into one `internal sealed record` in its own file under `tools/RustPlusBot.ItemData.Generator/`. Name it after what the group *is* (for example the set of dataset sources, or the set of output paths) — not `Args` or `Options`. Target 4 or fewer parameters on the method.

- [ ] **Step 4: Build and test**

Run: `dtk dotnet build && dtk dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: build clean, tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "refactor: group the generator's dataset parameters into a record

Clears sonar S107 in ItemData.Generator/Program.cs.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Split `ClanSnapshotDiffer.Diff` (S3776, complexity 21)

**Files:**
- Modify: `src/RustPlusBot.Features.Clans/State/ClanSnapshotDiffer.cs:15`
- Test: `tests/RustPlusBot.Features.Clans.Tests/State/ClanSnapshotDifferTests.cs`

**Interfaces:**
- Produces: `Diff(ClanSnapshot?, ClanSnapshot?)` keeps its exact public signature and its exact emission order. New private statics: `AddIdentityChanges(List<ClanChange>, ClanSnapshot, ClanSnapshot)`, `AddMembershipChanges(List<ClanChange>, ClanSnapshot, ClanSnapshot)`, `AddRoleChanges(List<ClanChange>, ClanSnapshot, ClanSnapshot)`.

- [ ] **Step 1: Write characterisation tests first**

Before touching the method, add tests to `ClanSnapshotDifferTests.cs` that pin the behaviour the split must preserve. Required cases, each asserting the full `IReadOnlyList<ClanChange>` including order:

```csharp
[Fact]
public void Diff_ReturnsNothing_WhenPreviousIsNull() { /* baseline, not news */ }

[Fact]
public void Diff_ReturnsDissolved_WhenCurrentIsNull() { }

[Fact]
public void Diff_ReturnsNothing_WhenClanIdChanged() { }

[Fact]
public void Diff_EmitsRenamedThenMotdThenJoinsThenLeavesThenRoles_InThatOrder() { }

[Fact]
public void Diff_ReportsInviteAcceptance_Once_NotAsJoinPlusRevocation() { }

[Fact]
public void Diff_OrdersJoinsBySteamId() { }

[Fact]
public void Diff_OrdersLeavesBySteamId() { }

[Fact]
public void Diff_SkipsRoleChange_WhenEitherRoleIdIsUnknown() { }
```

- [ ] **Step 2: Run them against the current implementation**

Run: `dtk dotnet test tests/RustPlusBot.Features.Clans.Tests/RustPlusBot.Features.Clans.Tests.csproj --filter ClanSnapshotDiffer`
Expected: PASS. If any fails, the test encodes the wrong expectation — fix the test, not the production code. This is the safety net; it must be green *before* the refactor.

- [ ] **Step 3: Commit the characterisation tests alone**

```bash
git add tests/RustPlusBot.Features.Clans.Tests && git commit -m "test: pin ClanSnapshotDiffer emission order before refactor

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 4: Extract the three private steps**

Keep the early returns (`previous is null`, `current is null`, `ClanId` mismatch) in `Diff`. Then `Diff` reads:

```csharp
var changes = new List<ClanChange>();
AddIdentityChanges(changes, previous, current);
AddMembershipChanges(changes, previous, current);
AddRoleChanges(changes, previous, current);
return changes;
```

`AddIdentityChanges` holds the Name and Motd comparisons. `AddMembershipChanges` holds the members/invites dictionaries, the `accepted` set, and the join and leave loops. `AddRoleChanges` holds the `rolesById` lookup and the role-change loop. Each new method gets an XML `<summary>` and `<param>` docs.

- [ ] **Step 5: Verify the tests still pass, unchanged**

Run: `dtk dotnet build && dtk dotnet test tests/RustPlusBot.Features.Clans.Tests/RustPlusBot.Features.Clans.Tests.csproj`
Expected: build clean (S3776 gone — SonarAnalyzer runs in-build, so a still-complex method fails the build), tests PASS with zero test edits.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor: split ClanSnapshotDiffer.Diff into identity, membership and role steps

Cognitive complexity 21 -> under 15. Emission order is unchanged and pinned by tests.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Split the two `WorkspaceReconciler` methods (S3776, complexity 41 and 21)

**Files:**
- Modify: `src/RustPlusBot.Features.Workspace/Reconciler/WorkspaceReconciler.cs:162` and `:276`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/`

- [ ] **Step 1: Read both methods in full**

```bash
sed -n '150,402p' src/RustPlusBot.Features.Workspace/Reconciler/WorkspaceReconciler.cs
```

- [ ] **Step 2: Baseline the workspace tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: PASS. Note the count.

- [ ] **Step 3: Add characterisation tests for any branch of the two methods not already covered**

`WorkspaceReconciler` is the hot spot of the whole feature; the method at line 276 has complexity 41, meaning many branches. Before splitting, list its decision points and confirm each is exercised by an existing test. Add tests for the ones that are not. Assert on observable outcomes — which channels get created, renamed, reordered or left alone — not on private call sequences.

- [ ] **Step 4: Run and commit the new characterisation tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: PASS.

```bash
git add tests/RustPlusBot.Features.Workspace.Tests && git commit -m "test: cover WorkspaceReconciler branches before refactor

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Split each method into named private steps**

Extract along the seams the method already has — each cohesive block that ends by mutating the reconcile outcome becomes one private method whose name states what it decides (for example `EnsureCategoryAsync`, `EnsureChannelAsync`, `PruneOrphanedChannelsAsync`, `ApplyChannelOrderAsync`). Pass state explicitly; do not introduce mutable fields to carry state between the extracted steps. Each extracted method gets XML docs.

- [ ] **Step 6: Verify**

Run: `dtk dotnet build && dtk dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: build clean (both S3776 instances gone), tests PASS with zero test edits.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "refactor: split WorkspaceReconciler's two complex methods into named steps

Cognitive complexity 41 and 21 -> under 15 each.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Split `DatasetValidator` (S3776, complexity 25)

**Files:**
- Modify: `tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs:139`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/Validation/`

- [ ] **Step 1: Read the method**

```bash
sed -n '130,219p' tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs
```

- [ ] **Step 2: Baseline the tests**

Run: `dtk dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: PASS.

- [ ] **Step 3: Add a test per validation rule the method enforces**

One `[Fact]` per rule: a dataset that violates exactly that rule produces exactly that diagnostic, and a valid dataset produces none. These double as the coverage win for this file.

- [ ] **Step 4: Extract one private method per validation rule**

Each returns its diagnostics (or appends to a passed-in list). The top-level method becomes a sequence of rule calls.

- [ ] **Step 5: Verify and commit**

Run: `dtk dotnet build && dtk dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: build clean, tests PASS.

```bash
git add -A && git commit -m "refactor: split DatasetValidator into one method per validation rule

Cognitive complexity 25 -> under 15, and each rule is now directly testable.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Phase B — Deduplication

### Task 7: `EventLoopHostedService` base, and migrate six hosted services (kills S1192 + the largest duplication cluster)

**Files:**
- Create: `src/RustPlusBot.Abstractions/Hosting/EventLoopHostedService.cs`
- Create: `src/RustPlusBot.Abstractions/Hosting/EventLoopRegistration.cs`
- Modify: `src/RustPlusBot.Abstractions/RustPlusBot.Abstractions.csproj` (add `Microsoft.Extensions.Hosting.Abstractions` + `Microsoft.Extensions.Logging.Abstractions` package references and `InternalsVisibleTo` for the Abstractions test project)
- Modify: `src/RustPlusBot.Features.Switches/Hosting/SwitchesHostedService.cs`, `src/RustPlusBot.Features.StorageMonitors/Hosting/StorageMonitorsHostedService.cs`, `src/RustPlusBot.Features.Alarms/Hosting/AlarmsHostedService.cs`, `src/RustPlusBot.Features.Players/Hosting/PlayersHostedService.cs`, `src/RustPlusBot.Features.Commands/Hosting/CommandsHostedService.cs`, `src/RustPlusBot.Features.Workspace/Hosting/WorkspaceHostedService.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/Hosting/EventLoopHostedServiceTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class EventLoopRegistration` with `public string Name { get; }` and an internal `Func<ILogger, CancellationToken, Task> RunAsync`.
  - `public abstract class EventLoopHostedService : IHostedService, IDisposable` with:
    - `protected EventLoopHostedService(IEventBus eventBus, ILogger logger)`
    - `protected abstract IEnumerable<EventLoopRegistration> Loops { get; }`
    - `protected EventLoopRegistration Loop<TEvent>(string name, Func<TEvent, CancellationToken, Task> handle) where TEvent : notnull`
    - `protected virtual void OnStarting()` and `protected virtual void OnStopping()` — hooks for services with extra wiring (`WorkspaceHostedService` uses them for its `DiscordSocketClient` event handlers)
    - `public Task StartAsync(CancellationToken)`, `public Task StopAsync(CancellationToken)`, `public void Dispose()`

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.Abstractions.Tests/Hosting/EventLoopHostedServiceTests.cs`. Use `InMemoryEventBus` (the real one — it is in the same assembly and already tested) rather than a mock bus, so the subscribe timing is genuinely exercised:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Hosting;

namespace RustPlusBot.Abstractions.Tests.Hosting;

public sealed class EventLoopHostedServiceTests
{
    private sealed record Ping(int Value);
    private sealed record Pong(int Value);

    private sealed class Subject(IEventBus bus) : EventLoopHostedService(bus, NullLogger.Instance)
    {
        public List<int> Pings { get; } = [];
        public List<int> Pongs { get; } = [];
        public int StartingCalls { get; private set; }
        public int StoppingCalls { get; private set; }
        public Func<Ping, Task>? OnPing { get; set; }

        protected override IEnumerable<EventLoopRegistration> Loops =>
        [
            Loop<Ping>("ping", async (e, _) =>
            {
                if (OnPing is not null) { await OnPing(e).ConfigureAwait(false); }
                Pings.Add(e.Value);
            }),
            Loop<Pong>("pong", (e, _) => { Pongs.Add(e.Value); return Task.CompletedTask; }),
        ];

        protected override void OnStarting() => StartingCalls++;
        protected override void OnStopping() => StoppingCalls++;
    }

    [Fact]
    public async Task StartAsync_SubscribesEagerly_SoEventsPublishedImmediatelyAfterStartAreNotDropped()
    {
        var bus = new InMemoryEventBus();
        var subject = new Subject(bus);
        await subject.StartAsync(CancellationToken.None);

        // No delay, no polling before publishing: this is the regression this base class exists to prevent.
        await bus.PublishAsync(new Ping(1));

        await WaitFor(() => subject.Pings.Count == 1);
        Assert.Equal([1], subject.Pings);
        await subject.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EveryDeclaredLoop_Runs()
    {
        var bus = new InMemoryEventBus();
        var subject = new Subject(bus);
        await subject.StartAsync(CancellationToken.None);
        await bus.PublishAsync(new Ping(1));
        await bus.PublishAsync(new Pong(2));

        await WaitFor(() => subject.Pings.Count == 1 && subject.Pongs.Count == 1);
        Assert.Equal([1], subject.Pings);
        Assert.Equal([2], subject.Pongs);
        await subject.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AThrowingHandler_CostsOneEvent_NotTheSubscription()
    {
        var bus = new InMemoryEventBus();
        var subject = new Subject(bus) { OnPing = e => e.Value == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask };
        await subject.StartAsync(CancellationToken.None);

        await bus.PublishAsync(new Ping(1)); // throws
        await bus.PublishAsync(new Ping(2)); // must still be handled

        await WaitFor(() => subject.Pings.Count == 1);
        Assert.Equal([2], subject.Pings);
        await subject.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_JoinsEveryLoop_AndDoesNotThrowOnCancellation()
    {
        var bus = new InMemoryEventBus();
        var subject = new Subject(bus);
        await subject.StartAsync(CancellationToken.None);
        await subject.StopAsync(CancellationToken.None);
        subject.Dispose();
    }

    [Fact]
    public async Task StopAsync_IsSafe_WhenStartAsyncWasNeverCalled()
    {
        var subject = new Subject(new InMemoryEventBus());
        await subject.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OnStartingAndOnStopping_AreInvokedExactlyOnce()
    {
        var bus = new InMemoryEventBus();
        var subject = new Subject(bus);
        await subject.StartAsync(CancellationToken.None);
        await subject.StopAsync(CancellationToken.None);
        Assert.Equal(1, subject.StartingCalls);
        Assert.Equal(1, subject.StoppingCalls);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition was not met within the timeout");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dtk dotnet test tests/RustPlusBot.Abstractions.Tests/RustPlusBot.Abstractions.Tests.csproj --filter EventLoopHostedService`
Expected: FAIL — `EventLoopHostedService` does not exist (compile error).

- [ ] **Step 3: Add the package references**

In `src/RustPlusBot.Abstractions/RustPlusBot.Abstractions.csproj` add an `ItemGroup` with `<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />` and `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />`. Both versions already exist in `Directory.Packages.props`. Add `<InternalsVisibleTo Include="RustPlusBot.Abstractions.Tests" />` if the test needs it (the new types are `public`, so it likely does not).

- [ ] **Step 4: Implement `EventLoopRegistration`**

```csharp
namespace RustPlusBot.Abstractions.Hosting;

/// <summary>One named event-consumption loop owned by an <see cref="EventLoopHostedService"/>.</summary>
public sealed class EventLoopRegistration
{
    internal EventLoopRegistration(string name, Func<ILogger, CancellationToken, Task> runAsync)
    {
        Name = name;
        RunAsync = runAsync;
    }

    /// <summary>Gets the loop's name, used in the "loop faulted" log message.</summary>
    public string Name { get; }

    internal Func<ILogger, CancellationToken, Task> RunAsync { get; }
}
```

Add the `using Microsoft.Extensions.Logging;` this needs.

- [ ] **Step 5: Implement `EventLoopHostedService`**

Requirements the tests above pin:

- `Loop<TEvent>(name, handle)` must call `eventBus.SubscribeAsync<TEvent>(token)` **at the moment `StartAsync` enumerates `Loops`**, synchronously, before any `Task.Run`. Then the returned stream is handed to `EventBusConsumption.ConsumeAsync(stream, handle, onFailure, token)` inside the `Task.Run`. This is the whole point of the class — do not subscribe inside the `Task.Run`.
  Implementation shape: `Loop<TEvent>` captures `handle` and returns a registration whose `RunAsync` closure does the subscribe-then-consume, and `StartAsync` calls `SubscribeAsync` eagerly. The simplest correct form is for `Loop<TEvent>` to eagerly capture the stream — but `Loops` is evaluated inside `StartAsync`, where the CTS token exists, so make `Loops` enumerate exactly once in `StartAsync` and materialise it to an array before starting any task.
- The handler-failure callback logs at `Error` with the message `"Handling {EventType} failed; skipping that event."` and `typeof(TEvent).Name`. Use a `[LoggerMessage]` partial (the class must then be `partial`) to match the codebase's logging style.
- Each loop task wraps its work in `try { ... } catch (OperationCanceledException) { /* shutting down */ } catch (Exception ex) { LogLoopFaulted(logger, ex, registration.Name); }` with `#pragma warning disable CA1031` and the justification comment `// Broad catch: a faulting consumer must not crash the host.`
- `StopAsync` cancels the CTS then awaits every started task, swallowing `OperationCanceledException`, with `#pragma warning disable VSTHRD003` and the comment `// Our own loop tasks, joined on stop.`
- `StopAsync` must be safe when `StartAsync` never ran (no tasks to join).
- `Dispose` disposes the CTS.
- `OnStarting()` is called first thing in `StartAsync`, before any subscription, so a subclass can do synchronous fail-fast work. `OnStopping()` is called first thing in `StopAsync`.

- [ ] **Step 6: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Abstractions.Tests/RustPlusBot.Abstractions.Tests.csproj --filter EventLoopHostedService`
Expected: PASS, all six.

- [ ] **Step 7: Commit the base class**

```bash
git add -A && git commit -m "feat: add EventLoopHostedService base for event-bus consumers

Subscribes eagerly in StartAsync, closing the start-up drop window that
EventBusConsumption's remarks warn about.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Migrate `SwitchesHostedService`**

It becomes a constructor plus a `Loops` table. Every `ConsumeXAsync` method, every `Task?` field, the CTS, `StartAsync`, `StopAsync`, `Dispose` and every `[LoggerMessage]` partial except any that are genuinely switch-specific all go away. Target shape:

```csharp
internal sealed class SwitchesHostedService(
    IEventBus eventBus,
    SwitchPairingCoordinator coordinator,
    SwitchStateRelay relay,
    SwitchWipePurger purger,
    ILogger<SwitchesHostedService> logger) : EventLoopHostedService(eventBus, logger)
{
    protected override IEnumerable<EventLoopRegistration> Loops =>
    [
        Loop<SwitchPairedEvent>("switch pairing", coordinator.HandlePairedAsync),
        Loop<SwitchStateChangedEvent>("switch state relay", relay.HandleStateChangedAsync),
        Loop<ConnectionStatusChangedEvent>("switch connection-status relay", relay.HandleConnectionStatusAsync),
        Loop<SmartDeviceTriggeredEvent>("switch device-triggered relay", relay.HandleDeviceTriggeredAsync),
        Loop<DeviceReachabilityChangedEvent>("switch reachability relay", relay.HandleReachabilityChangedAsync),
        Loop<ServerWipedEvent>("switch wipe-purge", purger.HandleServerWipedAsync),
    ];
}
```

`RustPlusBot.Features.Switches.csproj` already references Abstractions, so no new reference is needed.

- [ ] **Step 9: Run the switches tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Switches.Tests/RustPlusBot.Features.Switches.Tests.csproj`
Expected: PASS. If a test asserted on the old private loop methods, rewrite it to assert observable behaviour (publish on the bus, assert the collaborator was called).

- [ ] **Step 10: Migrate the remaining five the same way**

`StorageMonitorsHostedService`, `AlarmsHostedService`, `PlayersHostedService`, `CommandsHostedService` are direct equivalents.

`WorkspaceHostedService` is the exception: keep its `DiscordSocketClient` `Ready` and `ChannelDestroyed` subscriptions and its `IWorkspaceRegistry` fail-fast resolution by overriding `OnStarting()` (registry resolution + `client.Ready += ...` + `client.ChannelDestroyed += ...`) and `OnStopping()` (the two `-=`). Keep `_startupDone` and the `OnReadyAsync`/`OnChannelDestroyedAsync` handlers as they are. Its four consume loops move into `Loops`. **This is what removes the S1192 smell** — the four repetitions of `"Handling {EventType} failed; skipping that reconcile."` collapse into the base class's single message.

- [ ] **Step 11: Full build and test**

Run: `dotnet build && dotnet test`
Expected: build clean — in particular no S1192 — and the whole suite PASSES.

- [ ] **Step 12: Commit**

```bash
git add -A && git commit -m "refactor: move six hosted services onto EventLoopHostedService

Removes the duplicated consume-loop boilerplate across Switches,
StorageMonitors, Alarms, Players, Commands and Workspace, and clears
sonar S1192 in WorkspaceHostedService.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: `RustPlusBot.Features.Devices` and the generic pairing coordinator

**Files:**
- Create: `src/RustPlusBot.Features.Devices/RustPlusBot.Features.Devices.csproj`
- Create: `src/RustPlusBot.Features.Devices/Pairing/PairedDeviceCoordinator.cs`
- Create: `tests/RustPlusBot.Features.Devices.Tests/RustPlusBot.Features.Devices.Tests.csproj`
- Create: `tests/RustPlusBot.Features.Devices.Tests/Pairing/PairedDeviceCoordinatorTests.cs`
- Modify: `RustPlusBot.slnx`
- Modify: `src/RustPlusBot.Features.Switches/Pairing/SwitchPairingCoordinator.cs`, `src/RustPlusBot.Features.StorageMonitors/Pairing/StorageMonitorPairingCoordinator.cs`, `src/RustPlusBot.Features.Alarms/Pairing/AlarmPairingCoordinator.cs`
- Modify: the three feature `.csproj` files (add a `ProjectReference` to Devices)

**Interfaces:**
- Produces: `public abstract class PairedDeviceCoordinator<TPairedEvent, TEntity>` with:
  - `protected PairedDeviceCoordinator(IServiceScopeFactory scopeFactory, IDeviceChannelLocator locator, IDeviceChannelPoster poster)`
  - `public string? PendingName(ulong guildId, Guid serverId, ulong entityId)`
  - `public Task HandlePairedAsync(TPairedEvent evt, CancellationToken cancellationToken)`
  - `public Task<bool> TryAcceptAsync(ulong guildId, Guid serverId, ulong entityId, ulong acceptingUserId, CancellationToken cancellationToken)`
  - `public bool TryDismiss(ulong guildId, Guid serverId, ulong entityId)`
  - Abstract hooks, which are the *only* differences between the Switch and StorageMonitor versions:
    - `protected abstract string DefaultName(ulong entityId);`
    - `protected abstract (Embed Embed, MessageComponent Components) RenderPrompt(Guid serverId, ulong entityId, string defaultName, CultureInfo culture);`
    - `protected abstract (Embed Embed, MessageComponent Components) RenderAccepted(TEntity entity, CultureInfo culture);`
    - `protected abstract Task<bool> ExistsAsync(...)`, `protected abstract Task<TEntity> AddAsync(...)`, `protected abstract Task SetMessageIdAsync(...)` — each taking the scoped `IServiceProvider` so the store type stays feature-local.

- [ ] **Step 1: Confirm the structural equivalence claim**

```bash
diff <(sed 's/StorageMonitor/DEV/g' src/RustPlusBot.Features.StorageMonitors/Pairing/StorageMonitorPairingCoordinator.cs) \
     <(sed 's/Switch/DEV/g'         src/RustPlusBot.Features.Switches/Pairing/SwitchPairingCoordinator.cs)
```

Expected: the only substantive differences are the default-name prefix (`"Storage Monitor "` vs `"Switch "`), the accepted-render call (`renderer.RenderMonitor(added, contents: null, culture)` vs `renderer.RenderSwitch(added, isActive: added.LastIsActive, culture)`), the store interface, and doc-comment wording. **If anything else differs, stop and report it** — the collapse is only safe under this claim.

- [ ] **Step 2: Write characterisation tests against the two existing coordinators**

In `tests/RustPlusBot.Features.Switches.Tests/` and `tests/RustPlusBot.Features.StorageMonitors.Tests/`, cover each behaviour the generic base must preserve, using NSubstitute for the locator, poster, renderer and store:

- `HandlePairedAsync` returns without posting when the device is already managed.
- `HandlePairedAsync` returns without posting when the locator yields no channel.
- `HandlePairedAsync` posts the prompt and makes `PendingName` return the default name.
- `TryAcceptAsync` returns `false` and clears the pending entry when the device is already managed (the race guard).
- `TryAcceptAsync` persists, posts the accepted embed, and stores the returned message id.
- `TryAcceptAsync` persists but posts nothing when the locator yields no channel.
- `TryAcceptAsync` falls back to the default name when no pending entry is held.
- `TryAcceptAsync` does not call `SetMessageIdAsync` when the poster returns null.
- `TryDismiss` returns `true` when a pending entry existed, `false` otherwise.

- [ ] **Step 3: Run them green against the current code, then commit**

Run: `dotnet test tests/RustPlusBot.Features.Switches.Tests/ tests/RustPlusBot.Features.StorageMonitors.Tests/`
Expected: PASS.

```bash
git add tests && git commit -m "test: pin pairing-coordinator behaviour before the generic collapse

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 4: Create the Devices project**

`src/RustPlusBot.Features.Devices/RustPlusBot.Features.Devices.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="RustPlusBot.Features.Devices.Tests" />
    <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
    <ProjectReference Include="..\RustPlusBot.Discord\RustPlusBot.Discord.csproj" />
    <ProjectReference Include="..\RustPlusBot.Domain\RustPlusBot.Domain.csproj" />
    <ProjectReference Include="..\RustPlusBot.Localization\RustPlusBot.Localization.csproj" />
    <ProjectReference Include="..\RustPlusBot.Persistence\RustPlusBot.Persistence.csproj" />
    <ProjectReference Include="..\RustPlusBot.Features.Workspace\RustPlusBot.Features.Workspace.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Discord.Net" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>

</Project>
```

Add it to `RustPlusBot.slnx` alongside the other `src/` projects, and create the matching test project mirroring `tests/RustPlusBot.Features.Switches.Tests/*.csproj` (referencing Devices).

- [ ] **Step 5: Implement `PairedDeviceCoordinator<TPairedEvent, TEntity>`**

Port the body of `SwitchPairingCoordinator` verbatim, replacing the four varying points with the abstract hooks listed under Interfaces. Keep the `ConcurrentDictionary<(ulong Guild, Guid Server, ulong Entity), Pending>` and the nested `Pending(string DefaultName, ulong? MessageId)` record. Keep every comment that explains *why* (the race guard note, the "freshly accepted, state unknown" note — reword the latter generically). Full XML docs on every member.

- [ ] **Step 6: Reduce the three feature coordinators to subclasses**

`SwitchPairingCoordinator` becomes a subclass supplying `DefaultName(id) => $"Switch {id}"`, `RenderPrompt` delegating to `SwitchEmbedRenderer.RenderPrompt`, `RenderAccepted(entity, culture) => renderer.RenderSwitch(entity, entity.LastIsActive, culture)`, and the three store hooks resolving `ISwitchStore` from the scope. `StorageMonitorPairingCoordinator` mirrors it with `RenderMonitor(entity, contents: null, culture)`. Fold `AlarmPairingCoordinator`'s shared block in the same way if it fits the shape; if the alarm flow genuinely differs, leave it and note why in the commit message.

- [ ] **Step 7: Verify with the characterisation tests, unchanged**

Run: `dtk dotnet build && dtk dotnet test`
Expected: build clean, whole suite PASSES with zero edits to the Step 2 tests.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "refactor: collapse the device pairing coordinators onto a generic base

Adds RustPlusBot.Features.Devices as the shared home for smart-device
scaffolding. Switch and StorageMonitor pairing were structurally identical.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Share the Discord device-poster body

**Files:**
- Create: `src/RustPlusBot.Features.Devices/Posting/DiscordDeviceChannelPoster.cs`
- Modify: `src/RustPlusBot.Features.Switches/Posting/DiscordSwitchChannelPoster.cs`, `src/RustPlusBot.Features.StorageMonitors/Posting/DiscordStorageMonitorChannelPoster.cs`, `src/RustPlusBot.Features.Vending/Posting/DiscordVendingChannelPoster.cs`

These three files are 61, 61 and roughly 62 lines with 46, 46 and 35 duplicated lines respectively. They are in `sonar.coverage.exclusions` but **not** in `sonar.cpd.exclusions`, so they count against duplication.

- [ ] **Step 1: Read all three and confirm the shared body**

```bash
cat src/RustPlusBot.Features.Switches/Posting/DiscordSwitchChannelPoster.cs \
    src/RustPlusBot.Features.StorageMonitors/Posting/DiscordStorageMonitorChannelPoster.cs \
    src/RustPlusBot.Features.Vending/Posting/DiscordVendingChannelPoster.cs
```

- [ ] **Step 2: Extract the shared ensure/edit body**

Put the common implementation in `DiscordDeviceChannelPoster` in the Devices project. The three feature posters keep their own interface (`ISwitchChannelPoster` etc.) and become thin types that delegate to it. Do not merge the interfaces — they are separate DI registrations.

- [ ] **Step 3: Verify**

Run: `dtk dotnet build && dtk dotnet test`
Expected: build clean, suite PASSES.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "refactor: share the Discord device-poster body across switch, monitor and vending

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 10: `PairedDeviceStore<TEntity>` base in Persistence

**Files:**
- Create: `src/RustPlusBot.Persistence/Devices/PairedDeviceStore.cs`
- Modify: `src/RustPlusBot.Persistence/Switches/SwitchStore.cs:86-104`, `src/RustPlusBot.Persistence/StorageMonitors/StorageMonitorStore.cs:85-103`
- Test: `tests/RustPlusBot.Persistence.Tests/`

- [ ] **Step 1: Read the two duplicated blocks**

```bash
sed -n '80,110p' src/RustPlusBot.Persistence/Switches/SwitchStore.cs
sed -n '79,109p' src/RustPlusBot.Persistence/StorageMonitors/StorageMonitorStore.cs
```

- [ ] **Step 2: Baseline the persistence tests**

Run: `dtk dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj`
Expected: PASS.

- [ ] **Step 3: Extract the shared block**

Both entities derive from `PairedEntity` (see `src/RustPlusBot.Domain/Entities/PairedEntity.cs`), so a generic base constrained to `where TEntity : PairedEntity` can hold the shared query. Move only the 19-line duplicated block; leave everything feature-specific in place.

- [ ] **Step 4: Verify and commit**

Run: `dtk dotnet build && dtk dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj`
Expected: build clean, tests PASS.

```bash
git add -A && git commit -m "refactor: share the paired-device query between the switch and monitor stores

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 11: Share the clan renderers' embed shell

**Files:**
- Modify: `src/RustPlusBot.Features.Clans/Messages/ClanOverviewMessageRenderer.cs:25-51`, `src/RustPlusBot.Features.Clans/Messages/ClanInvitesMessageRenderer.cs:22-48`
- Create: `src/RustPlusBot.Features.Clans/Messages/ClanMessageShell.cs`
- Test: `tests/RustPlusBot.Features.Clans.Tests/Messages/`

- [ ] **Step 1: Read both blocks and identify what varies**

```bash
sed -n '20,55p' src/RustPlusBot.Features.Clans/Messages/ClanOverviewMessageRenderer.cs
sed -n '18,52p' src/RustPlusBot.Features.Clans/Messages/ClanInvitesMessageRenderer.cs
```

- [ ] **Step 2: Baseline the clan tests**

Run: `dotnet test tests/RustPlusBot.Features.Clans.Tests/RustPlusBot.Features.Clans.Tests.csproj`
Expected: PASS.

- [ ] **Step 3: Extract the 27-line shell**

Create an internal static `ClanMessageShell` holding the shared embed construction, parameterised by the parts that differ (title key, the rendered body lines). Both renderers call it.

- [ ] **Step 4: Verify and commit**

Run: `dotnet build && dotnet test tests/RustPlusBot.Features.Clans.Tests/RustPlusBot.Features.Clans.Tests.csproj`
Expected: build clean, tests PASS with no assertion changes.

```bash
git add -A && git commit -m "refactor: share the clan embed shell between the overview and invites renderers

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 12: Make `StorageMonitorEmbedRenderer` use `DurationFormat`

**Files:**
- Modify: `src/RustPlusBot.Features.StorageMonitors/Rendering/StorageMonitorEmbedRenderer.cs:120-132`
- Test: `tests/RustPlusBot.Features.StorageMonitors.Tests/Rendering/`

The renderer re-implements `DurationFormat.Compact` (`src/RustPlusBot.Abstractions/Formatting/DurationFormat.cs:12-24`) inline — a 13-line duplicated block.

- [ ] **Step 1: Read both and confirm they are behaviourally identical**

```bash
sed -n '112,140p' src/RustPlusBot.Features.StorageMonitors/Rendering/StorageMonitorEmbedRenderer.cs
sed -n '8,26p' src/RustPlusBot.Abstractions/Formatting/DurationFormat.cs
```

Expected: both produce `"Xd Yh"` / `"Yh Zm"` / `"Zm"` with `CultureInfo.InvariantCulture`. **If they differ in any boundary case, stop and report** rather than silently changing rendered output.

- [ ] **Step 2: Add a test pinning the renderer's current duration output**

Include the day, hour and minute boundaries (for example 25 h, 90 min, 45 s).

- [ ] **Step 3: Run it green, then delete the inline copy**

Replace the inline formatting with a `DurationFormat.Compact(...)` call and remove the now-dead private method. `RustPlusBot.Features.StorageMonitors` already references Abstractions.

- [ ] **Step 4: Verify and commit**

Run: `dtk dotnet build && dtk dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests/RustPlusBot.Features.StorageMonitors.Tests.csproj`
Expected: build clean, tests PASS including the Step 2 test.

```bash
git add -A && git commit -m "refactor: use DurationFormat.Compact in the storage-monitor renderer

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 13: Extract the repeated relay guard

**Files:**
- Modify: `src/RustPlusBot.Features.Switches/Relaying/SwitchStateRelay.cs:34-48` and `:67-81`
- Modify: `src/RustPlusBot.Features.Events/Relaying/EventRelay.cs`, `src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs`
- Test: the matching relay tests in each feature's test project

`SwitchStateRelay` duplicates a 15-line block against itself; `EventRelay` (19 lines) and `PlayerEventRelay` (20 lines) carry the same shape.

- [ ] **Step 1: Read all three and confirm the shared shape**

```bash
sed -n '25,95p' src/RustPlusBot.Features.Switches/Relaying/SwitchStateRelay.cs
cat src/RustPlusBot.Features.Events/Relaying/EventRelay.cs
cat src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs
```

- [ ] **Step 2: Baseline the three test projects**

Run: `dotnet test tests/RustPlusBot.Features.Switches.Tests/ tests/RustPlusBot.Features.Events.Tests/ tests/RustPlusBot.Features.Players.Tests/`
Expected: PASS.

- [ ] **Step 3: Extract**

Fix `SwitchStateRelay`'s self-duplication with a private helper first. If the `EventRelay`/`PlayerEventRelay` shape genuinely matches, promote the helper to the Devices project; if the three only look alike superficially, fix each locally rather than forcing a shared abstraction. Prefer three small local helpers over one contorted shared one.

- [ ] **Step 4: Verify and commit**

Run: `dtk dotnet build && dtk dotnet test`
Expected: build clean, suite PASSES.

```bash
git add -A && git commit -m "refactor: extract the repeated relay guard

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 14: Extract the module self-duplication

**Files:**
- Modify: `src/RustPlusBot.Features.Vending/Modules/VendingModule.cs:81`, `:133`, `:192`, `:235` (an 18-line block repeated four times and a 20-line block repeated twice)
- Modify: `src/RustPlusBot.Features.Alarms/Modules/AlarmComponentModule.cs:79` and `:114` (a 21-line block repeated)

These files land inside the `**/Modules/*.cs` cpd exclusion added in Task 15, so this task is not strictly needed for the metric. Do it anyway: repeating an 18-line block four times inside one file is real duplication, and the extraction is low-risk.

- [ ] **Step 1: Read the repeated blocks**

```bash
sed -n '75,105p;127,155p;186,215p;229,258p' src/RustPlusBot.Features.Vending/Modules/VendingModule.cs
sed -n '75,140p' src/RustPlusBot.Features.Alarms/Modules/AlarmComponentModule.cs
```

- [ ] **Step 2: Extract each repeated block into a private helper**

Keep the Discord attributes on the interaction methods exactly as they are — only the shared body moves.

- [ ] **Step 3: Verify and commit**

Run: `dtk dotnet build && dtk dotnet test`
Expected: build clean, suite PASSES.

```bash
git add -A && git commit -m "refactor: extract the repeated guard bodies in the vending and alarm modules

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 15: Update the Sonar configuration

**Files:**
- Modify: `.github/workflows/Sonar.yml:63`

- [ ] **Step 1: Extend `sonar.cpd.exclusions`**

From `**/Migrations/*.cs` to `**/Migrations/*.cs,**/Configurations/*.cs,**/Modules/*.cs`.

Rationale to put in the commit message: `Configurations/` is EF entity configuration and `Modules/` is Discord attribute-driven interaction wiring; in both the repetition is the framework's required shape, not logic.

- [ ] **Step 2: Extend `sonar.coverage.exclusions`**

Append `,**/RustPlusSocketSource.cs,**/RustPlusFcmPairingSource.cs,**/DiscordChatWebhookPoster.cs,**/DiscordClanFeedPoster.cs,**/ServerAutocompleteHandler.cs` to the existing list. Keep every existing entry.

- [ ] **Step 3: Verify the YAML is still valid**

```bash
python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/Sonar.yml')); print('ok')"
```

Expected: `ok`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/Sonar.yml && git commit -m "chore: narrow sonar scope to code with testable seams

cpd: exclude EF entity configurations and Discord interaction modules,
where the repetition is the framework's shape rather than logic.
coverage: exclude five adapters over the RustPlusApi socket, the FCM
listener and Discord.Net that have no injectable seam.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Phase C — Coverage

Every task in this phase follows the same rhythm: pick the file, read its uncovered lines, write tests that assert observable behaviour, confirm the coverage moved. Do **not** write tests that assert on private call sequences to inflate the number — a test that would not catch a real regression is worse than no test.

### Task 16: `ConnectionSupervisor` (153 uncovered lines, 38 uncovered conditions)

**Files:**
- Test: `tests/RustPlusBot.Features.Connections.Tests/Supervisor/ConnectionSupervisorTests.cs` (extend)

- [ ] **Step 1: Get the exact uncovered lines**

Use the SonarQube MCP tool `get_file_coverage_details` with key `HandyS11_RustPlusBot_efd0a2f9-c22b-41c4-a6d0-c3f25a4a3178:src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`, or read the local opencover report after a coverage run.

- [ ] **Step 2: Read the uncovered regions and the existing tests**

The file is 699 lines to cover and already 78% covered, so the existing test file shows the established fake/harness pattern. Reuse it — do not invent a second harness.

- [ ] **Step 3: Write one test per uncovered branch**

Prioritise reconnect, teardown, and error paths — those are the uncovered ones, and per the project memory they are exactly where this bot has had live outages (a connect-time timeout once permanently killed the reconnect loop). Tests here have real value beyond the metric.

- [ ] **Step 4: Verify coverage moved**

```bash
dotnet test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --collect:"XPlat Code Coverage;Format=opencover"
```

Then inspect the generated `coverage.opencover.xml` for `ConnectionSupervisor` and confirm the uncovered count dropped.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "test: cover ConnectionSupervisor's reconnect and teardown paths

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 17: The vending feature (260 uncovered lines across four files)

**Files:**
- Test: `tests/RustPlusBot.Persistence.Tests/Vending/VendingStoreTests.cs` (127 uncovered, 27 uncovered conditions)
- Test: `tests/RustPlusBot.Features.Vending.Tests/Hosting/VendingHostedServiceTests.cs` (53 uncovered)
- Test: `tests/RustPlusBot.Features.Vending.Tests/Tracking/VendingTrackServiceTests.cs` (40 uncovered, 6 uncovered conditions)
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/` for `VTrackCommandHandler` and `VUntrackCommandHandler` (11 uncovered each, 4 uncovered conditions each)

- [ ] **Step 1: Read each file's uncovered regions**

- [ ] **Step 2: `VendingStore` — the biggest single win**

`tests/RustPlusBot.Persistence.Tests/` already has a SQLite-backed test pattern for the other stores. Follow it. Cover add, update, remove, the tracked-item queries, and the "already tracked" and "not found" branches.

- [ ] **Step 3: `VendingTrackService` and `VendingHostedService`**

`VendingHostedService` moves onto `EventLoopHostedService` in Task 7 only if it fits that shape; if it did not, test it directly. Cover the notification path and the not-tracked path.

- [ ] **Step 4: The two command handlers**

Each has 4 uncovered conditions — the guard branches (no server, not tracked, already tracked, invalid input). One `[Fact]` per branch.

- [ ] **Step 5: Verify and commit**

```bash
dtk dotnet test tests/RustPlusBot.Persistence.Tests/ tests/RustPlusBot.Features.Vending.Tests/ tests/RustPlusBot.Features.Commands.Tests/
git add -A && git commit -m "test: cover the vending store, track service, hosted service and commands

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 18: The remaining hosted services (227 uncovered lines)

**Files:**
- Test: `tests/RustPlusBot.Features.Events.Tests/` — `EventsHostedService` (64 uncovered, 8 uncovered conditions)
- Test: `tests/RustPlusBot.Features.Workspace.Tests/` — `WorkspaceHostedService` (59 uncovered, 6 uncovered conditions), `ServerInfoRefreshHostedService` (31 uncovered, 4 uncovered conditions)
- Test: `tests/RustPlusBot.Features.Map.Tests/` — `MapHostedService` (42 uncovered, 9 uncovered conditions)
- Test: `tests/RustPlusBot.Features.Chat.Tests/` — `ChatHostedService` (31 uncovered, 11 uncovered conditions)

- [ ] **Step 1: Note what Task 7 already bought**

After the `EventLoopHostedService` migration, the loop machinery in each of these is covered by `EventLoopHostedServiceTests`. What remains uncovered is each service's own handlers and hooks. Re-run coverage first so this task targets what is actually still red, not the pre-refactor list.

```bash
dotnet test --collect:"XPlat Code Coverage;Format=opencover"
```

- [ ] **Step 2: Test each service's own logic**

For `WorkspaceHostedService`: the `OnReadyAsync` once-only guard (`_startupDone`), the `OnChannelDestroyedAsync` self-heal, and the `OnStarting` fail-fast when `IWorkspaceRegistry` cannot be resolved. For `ChatHostedService` (11 uncovered conditions on 12): the message-filtering branches. For `MapHostedService` and `EventsHostedService`: the refresh and dispatch branches.

- [ ] **Step 3: Verify and commit**

```bash
dtk dotnet test
git add -A && git commit -m "test: cover the events, workspace, map and chat hosted services

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 19: The command-handler branch tail

**Files:**
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/`

These files have high line coverage but uncovered *conditions*. Sonar's `coverage` metric counts both, so each uncovered condition costs. Targets, with uncovered condition counts: `ChinookCommandHandler` (6 uncovered lines), `RecycleCommandHandler` (4), `UpkeepCommandHandler` (3), `EventsCommandHandler` (4), `VendingCommandHandler` (3), `VTrackedCommandHandler` (2), `SteamIdCommandHandler` (2), `ItemCommandHandler` (1), `DurabilityCommandHandler` (1), `SmeltCommandHandler` (1), `CraftCommandHandler` (1), `DecayCommandHandler` (1), `ResearchCommandHandler` (1), `CctvCommandHandler` (1), `OfflineCommandHandler` (1), `TeamCommandHandler` (1), `WipeCommandHandler` (1), `RigReply` (3), `ItemLine` (1), `CommandCooldown` (2), `CommandContext` (1).

- [ ] **Step 1: For each handler, read the uncovered branch**

They are almost all the same shape: a guard that returns a localized "not found" / "no server" / "invalid argument" reply. The existing tests cover the happy path only.

- [ ] **Step 2: Add one `[Fact]` per uncovered branch**

Assert the localized reply key, not the rendered English string, so the tests survive copy changes.

- [ ] **Step 3: Verify and commit**

```bash
dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj
git add -A && git commit -m "test: cover the command handlers' guard branches

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 20: The remaining small holes

**Files:**
- Test: across `tests/RustPlusBot.Features.Clans.Tests/`, `tests/RustPlusBot.Persistence.Tests/`, `tests/RustPlusBot.Features.Map.Tests/`, `tests/RustPlusBot.Features.Workspace.Tests/`, `tests/RustPlusBot.Abstractions.Tests/`, `tests/RustPlusBot.Discord.Tests/`, `tests/RustPlusBot.Features.Events.Tests/`, `tests/RustPlusBot.ItemData.Generator.Tests/`

Targets: `ClanChangeRenderer` (20 uncovered lines, 23 uncovered conditions — the largest remaining), `MapSettingsStore` (9 + 5), `FcmRegistrationStore` (10 + 1), `WorkspaceStore` (10 + 11), `MapIcons` (3 + 9), `MapRenderStyle` (3 + 5), `ClanSnapshotSerializer` (3 + 2), `ChannelEditPacer` (6 + 2), `DatabaseMaintenanceService` (3 + 2), `PairingSupervisor` (20 + 5), `RustMapsGenerationDriver` (11 + 7), `EmbeddedItemDatabase` (5 conditions), `MapLocation` (3 + 3), `ActiveMarker` (1), `PlayerPlacement` (1), `PairedEntity` (1), `CommandContext` (1), `MapMarkersSnapshot` (1), `EventBusConsumption` (2), `VendingNotification` (2), `VendingStockNotification` (2), `IVendingTrackService` (1), `ServerWipedEvent` (1), `MapSettingsChangedEvent` (1), `MaintenanceOptions` (1), `ClanInfoChannelLocator` (1), `VendingChannelLocator` (1), `ClanChatChannelLocator` (6 + 4), `OfflineSmeltingSource` (3 + 9), `OfflineCctvSource` (1 + 3).

- [ ] **Step 1: Work down the list largest-first**

`ClanChangeRenderer` first — 23 uncovered conditions means most of its per-change-kind rendering is untested. One test per `ClanChangeKind`.

- [ ] **Step 2: The one-line records and locators**

These are single uncovered lines in records and one-line locator expressions. A single construction-and-read assertion covers each. Batch them into one test file per project.

- [ ] **Step 3: Re-measure after each project**

```bash
dotnet test --collect:"XPlat Code Coverage;Format=opencover"
```

- [ ] **Step 4: Commit per project**

```bash
git add -A && git commit -m "test: close the remaining coverage holes in <project>

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 21: Final local verification

**Files:** none — measurement only.

- [ ] **Step 1: Clean build with every analyser**

```bash
dotnet clean && dotnet build --configuration Release
```

Expected: zero warnings, zero errors. This is a **regression check only**. It does NOT prove the smells are cleared — see the correction in Global Constraints. The binding evidence for all 8 smells is Step 6's SonarQube query.

- [ ] **Step 2: Full suite with coverage**

```bash
dotnet test --configuration Release --collect:"XPlat Code Coverage;Format=opencover" --blame-hang-timeout 60s
```

Expected: every test passes. Note the total count.

- [ ] **Step 3: Compute the coverage number the way Sonar does**

Aggregate every `tests/*/TestResults/*/coverage.opencover.xml`, applying the `sonar.coverage.exclusions` patterns from `.github/workflows/Sonar.yml` so the local number is comparable to Sonar's. Sonar's `coverage` is `(coveredLines + coveredConditions) / (totalLines + totalConditions)` — compute both terms, not lines alone.

- [ ] **Step 4: Record the result honestly**

Write the measured coverage, the remaining uncovered files, and any residual duplication into the PR description. If coverage is below 90, go back to Phase C and close more holes before opening the PR. If a duplicated block survives, name it.

- [ ] **Step 5: Commit any final fixes, then open the PR**

```bash
git push -u origin chore/sonar-zero-smells-zero-dup-90-coverage
```

PR body must state: the three metric targets, the measured local numbers, the deliberate eager-subscribe behaviour change from Task 7, the two new exclusion patterns and why, and the new `RustPlusBot.Features.Devices` project.

- [ ] **Step 6: Confirm against the real analysis**

After CI runs, query SonarQube for the PR (`list_pull_requests` then `get_component_measures` with `pullRequest`) and confirm `code_smells=0`, `duplicated_lines_density=0`, `coverage>90`. **Do not claim success before this returns.** If any target is missed, the gap is reported, not rounded off.

---

## Self-review notes

- Spec coverage: all 8 smells map to Tasks 1-7 (S1192 lands in Task 7). All 8 duplication clusters map to Tasks 7-15. The coverage plan maps to Tasks 16-20. The Sonar config change is Task 15. Verification is Task 21.
- The `EventLoopHostedService` surface used in Tasks 7 and 18 matches the definition in Task 7's Interfaces block. `PairedDeviceCoordinator` in Tasks 8 and 9 matches its Interfaces block.
- Tasks 4, 5, 8 and 12 write characterisation tests *before* the refactor, because those four have no existing guard against a behaviour change.
