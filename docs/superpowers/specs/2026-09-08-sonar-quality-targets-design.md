# Design: drive SonarQube to 0 smells, 0% duplication, >90% coverage

Date: 2026-09-08
Branch: `chore/sonar-zero-smells-zero-dup-90-coverage`
Project key: `HandyS11_RustPlusBot_efd0a2f9-c22b-41c4-a6d0-c3f25a4a3178`

## Goal

Three measured targets on the `develop` branch analysis:

| Metric | Baseline | Target |
| --- | --- | --- |
| Code smells | 8 | 0 |
| Duplicated lines (%) | 3.5 (1215 lines, 58 blocks, 29 files) | 0 |
| Coverage | 82.8 (9435 to cover, 1530 uncovered) | >90, as close to 100 as the seams allow |

Behaviour must not change, except where this document says otherwise and explains why.

## Baseline detail

### The 8 smells

| Rule | File | Detail |
| --- | --- | --- |
| S3776 | `Features.Workspace/Reconciler/WorkspaceReconciler.cs:276` | Cognitive complexity 41 |
| S3776 | `Features.Workspace/Reconciler/WorkspaceReconciler.cs:162` | Cognitive complexity 21 |
| S3776 | `Features.Clans/State/ClanSnapshotDiffer.cs:15` | Cognitive complexity 21 |
| S3776 | `tools/ItemData.Generator/Validation/DatasetValidator.cs:139` | Cognitive complexity 25 |
| S107 | `Features.Map/Rendering/MapRenderer.cs:50` | 9 parameters |
| S107 | `Features.Commands/Handlers/MarkerReply.cs:25` | 8 parameters |
| S107 | `tools/ItemData.Generator/Program.cs:140` | 8 parameters |
| S1192 | `Features.Workspace/Hosting/WorkspaceHostedService.cs:181` | Literal `"Handling {EventType} failed; skipping that reconcile."` used 4 times |

### The duplication clusters

Confirmed against `get_duplications`, not guessed:

1. **Hosted-service consume loops** — `SwitchesHostedService` <-> `StorageMonitorsHostedService` (47-line block) and a 27-line block shared by Switches, StorageMonitors and Alarms. `PlayersHostedService` (37) and `CommandsHostedService` (37) carry the same shape.
2. **Pairing coordinators** — `SwitchPairingCoordinator` (142 lines) and `StorageMonitorPairingCoordinator` (143 lines) are structurally identical; `AlarmPairingCoordinator` shares a 22-line block.
3. **Discord device posters** — `DiscordSwitchChannelPoster` (46), `DiscordStorageMonitorChannelPoster` (46), `DiscordVendingChannelPoster` (35).
4. **Device stores** — `SwitchStore:86` <-> `StorageMonitorStore:85`, 19 lines.
5. **Clan renderers** — `ClanOverviewMessageRenderer:25` <-> `ClanInvitesMessageRenderer:22`, 27 lines.
6. **Duration formatting** — `StorageMonitorEmbedRenderer:120` re-implements `DurationFormat:12`, 13 lines.
7. **Self-duplication** — `SwitchStateRelay` (15 lines against itself), `VendingModule` (6 blocks), `AlarmComponentModule` (21 lines against itself). `EventRelay` and `PlayerEventRelay` share the relay guard.
8. **Declarative wiring** — `SmartSwitchConfiguration` <-> `SmartStorageMonitorConfiguration` (20 lines); Discord component modules across Alarms, StorageMonitors, Switches, Map, Commands.

### The coverage holes

Largest uncovered-line counts: `RustPlusSocketSource` 290, `ConnectionSupervisor` 153, `VendingStore` 127, `RustPlusFcmPairingSource` 88, `EventsHostedService` 64, `WorkspaceHostedService` 59, `VendingHostedService` 53, `MapHostedService` 42, `VendingTrackService` 40, `DiscordChatWebhookPoster` 32, `SwitchesHostedService` 32, `ServerInfoRefreshHostedService` 31, `ChatHostedService` 31, `AlarmsHostedService` 31, `StorageMonitorsHostedService` 27.

Sonar's `coverage` blends line and condition coverage. A long tail of command handlers sits at 82-89% with fully covered lines but uncovered guard branches; closing those is branch work, not line work.

## Architecture

### `EventLoopHostedService` (new, `RustPlusBot.Abstractions/Hosting/`)

Every feature hosted service is the same object: a set of event-bus consume loops started in `StartAsync` and joined in `StopAsync`. Today each one hand-rolls a `CancellationTokenSource`, one `Task?` field per loop, one `ConsumeXAsync` method per loop with an identical try/catch, and its own `LogHandlerFailed` partial.

Replace with an abstract base:

```csharp
public abstract class EventLoopHostedService(IEventBus eventBus, ILogger logger)
    : IHostedService, IDisposable
{
    protected abstract IEnumerable<EventLoopRegistration> Loops { get; }
    protected EventLoopRegistration Loop<TEvent>(
        string name, Func<TEvent, CancellationToken, Task> handle) where TEvent : notnull;
}
```

The base owns the CTS, the `Task.Run` fan-out, the join, the `OperationCanceledException` swallow, the broad-catch loop-faulted log, and the per-event handler-failure log. Subclasses shrink to a constructor and a `Loops` table.

This resolves cluster 1 and the S1192 smell together, and makes the loop machinery covered by one test class instead of five.

**Deliberate behaviour change.** `EventBusConsumption`'s own remarks warn that a hosted service which subscribes inside its background `Task.Run` is only subscribed once that task is scheduled, and drops every event published in the meantime. `SwitchesHostedService` and its siblings do exactly that today. The base will call `IEventBus.SubscribeAsync<TEvent>` eagerly and synchronously in `StartAsync`, then hand the stream to `ConsumeAsync` inside `Task.Run` — the pattern the remarks prescribe. This closes a real start-up drop window.

### `RustPlusBot.Features.Devices` (new project)

Switches, StorageMonitors and Alarms are parallel assemblies with `internal` types and no shared home, which is why their pairing, posting and relay code is copied rather than shared. Add `src/RustPlusBot.Features.Devices/` referencing Abstractions, Domain, Persistence, Discord, Workspace and Localization; the three feature projects reference it.

It holds:

- `PairedDeviceCoordinator<TPairedEvent, TEntity>` — the confirmed-identical pairing flow (pending dictionary, exists-guard, prompt post, race-guarded accept, dismiss). Two hooks cover every difference between the Switch and StorageMonitor versions: a `DefaultNamePrefix` string and a `RenderAccepted(TEntity, CultureInfo)` method returning the embed and components.
- `DiscordDeviceChannelPoster` — the shared ensure/edit/delete body behind the three `Discord*ChannelPoster` types, which become thin typed wrappers.
- The shared relay guard used by `SwitchStateRelay`, `EventRelay` and `PlayerEventRelay`.

### `PairedDeviceStore<TEntity>` (new, `RustPlusBot.Persistence`)

Base for the 19-line block shared by `SwitchStore` and `StorageMonitorStore`.

### Smaller extractions

- `StorageMonitorEmbedRenderer` drops its inline duration formatting and calls `DurationFormat.Compact`.
- The 27-line embed shell shared by the two clan renderers moves to a private helper in `Features.Clans/Messages/`.
- `SwitchStateRelay`, `VendingModule` and `AlarmComponentModule` get their self-duplicated blocks extracted into local helpers.

### Smell refactors

`WorkspaceReconciler` (both methods), `ClanSnapshotDiffer` and `DatasetValidator` split into named private steps. The split is chosen so each step is independently callable, which turns complexity reduction into coverage surface.

`MarkerReply`, `MapRenderer` and the generator's `Program` take a parameter record instead of 8-9 positional parameters.

## Sonar configuration changes

In `.github/workflows/Sonar.yml`:

- `sonar.cpd.exclusions` gains `**/Configurations/*.cs` and `**/Modules/*.cs`. These are declarative EF entity configuration and Discord attribute-driven interaction wiring, where the repetition is the framework's required shape rather than logic. `**/Migrations/*.cs` stays.
- `sonar.coverage.exclusions` gains `**/RustPlusSocketSource.cs`, `**/RustPlusFcmPairingSource.cs`, `**/DiscordChatWebhookPoster.cs`, `**/DiscordClanFeedPoster.cs`, `**/ServerAutocompleteHandler.cs` — thin adapters over the RustPlusApi socket, the FCM listener and Discord.Net with no injectable seam. Together they account for 441 of the 1530 uncovered lines.

Every other file stays in scope and gets real tests.

## Testing

New and extended test coverage, in rough priority order by uncovered lines:

1. `EventLoopHostedService` — start subscribes eagerly, loops run, a throwing handler costs one event and not the subscription, cancellation ends cleanly, stop joins every loop. Covers the machinery behind five feature hosted services at once.
2. `ConnectionSupervisor` (153 uncovered, 38 uncovered conditions) — the largest testable hole.
3. `VendingStore` (127) and the vending feature: `VendingHostedService` (53), `VendingTrackService` (40), `VTrack`/`VUntrack` handlers.
4. `EventsHostedService` (64), `WorkspaceHostedService` (59), `MapHostedService` (42), `ChatHostedService` (31), `ServerInfoRefreshHostedService` (31).
5. `PairedDeviceCoordinator` — one test class replacing what would otherwise be three.
6. The command-handler tail: guard branches in `Chinook`, `Recycle`, `Upkeep`, `Item`, `Durability`, `Smelt`, `Craft`, `Decay`, `Research`, `Cctv`, `Events`, `Offline`, `Team`, `Wipe`, `SteamId`, `Vending`.
7. Small holes: `ClanChangeRenderer` (20 uncovered, 23 uncovered conditions), `MapSettingsStore`, `FcmRegistrationStore`, `WorkspaceStore`, `MapIcons`, `MapRenderStyle`, `ClanSnapshotSerializer`, `ChannelEditPacer`, the locators, and the one-line event/record types.

Refactors are behaviour-preserving and guarded by the existing suite. Where a refactor has no existing guard (the pairing coordinator collapse, the hosted-service base), characterisation tests land before the refactor.

## Verification

Local, before the PR:

```
dtk dotnet test --collect:"XPlat Code Coverage;Format=opencover"
```

Aggregate the opencover reports and compute line and branch coverage; confirm the build is clean under `TreatWarningsAsErrors` with the full analyser set (NetAnalyzers, Roslynator, SonarAnalyzer, VS Threading) at `AnalysisLevel=latest-all`. The SonarAnalyzer package runs in-build, so S3776/S107/S1192 regressions surface as build errors locally rather than only in the Sonar report.

Final confirmation comes from the Sonar analysis of the PR and, after merge, of `develop`.

## Delivery

One branch off `develop`, commits separated by concern (smells, each dedup cluster, coverage), one PR.

## Risks

- **The generic device coordinator** is the riskiest collapse. Mitigated by the confirmed structural diff (only the name prefix and the render call differ) and by characterisation tests written before the change.
- **The eager-subscribe change** alters start-up timing for five hosted services. It is the documented intent of `EventBusConsumption` and closes a real drop window, but it is a behaviour change and is called out in the PR description.
- **Coverage of 100% is not promised.** The exclusion list is deliberately narrow, so some genuinely awkward paths remain in scope. The commitment is >90% with an honest report of where it lands.
- **Duplication of exactly 0%** depends on Sonar's block detector; after the refactor a residual small block may remain. If one survives, it gets fixed or explicitly reported, not silently excluded beyond the two agreed exclusion patterns.
