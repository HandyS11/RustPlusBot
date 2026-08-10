# Patrol Helicopter Crash Reporting and Off-Map Directions — Design

Date: 2026-08-10
Status: Approved

## Problem

Two defects in live map-event reporting.

**1. A downed patrol helicopter is reported as having left.**
`MarkerEventClassifier` maps every patrol-helicopter marker removal to `MapEventKind.HeliLeft`
(`src/RustPlusBot.Features.Events/Classifying/MarkerEventClassifier.cs:38-41`), rendered as
"🚁 Patrol Helicopter left (D7)". The heli disappears from the map for two very different reasons:
it was shot down, or it finished its patrol and flew off the map edge. The shoot-down case is the
one players care about — it marks loot on the ground — and it is currently indistinguishable from a
routine departure.

**2. Off-map markers report a fake grid cell.**
`MapGrid.LabelFor` clamps out-of-world coordinates to the nearest edge cell
(`src/RustPlusBot.Abstractions/Connections/MapGrid.cs:73-74`). Cargo ships, helicopters and chinooks
spawn in the ocean *outside* the playable world, so their spawn announcement names a cell they are
not in and may never visit. Players describe these spawns by direction ("cargo spawned north-east"),
not by cell.

## Goals

- Report a heli that disappeared inside the map as a probable crash, with its grid cell.
- Report a heli that disappeared at or beyond the map border as having left, with a direction.
- Report any marker positioned outside the playable world by 8-point compass direction rather than a
  clamped grid cell, across announcements, team-chat lines, commands and the `#info` embed.
- Keep both supported cultures (`en`, `fr`) naturally worded.

## Non-goals

- No change to marker polling, state storage, or map rendering.
- No change to team-member position formatting — players are always inside the world.
- No new user-facing settings. The border band is a constant, not a per-server option.

## Decisions

| Question | Decision |
| --- | --- |
| Crash vs left | Position-based with a tolerance band: inside the map by more than one grid cell → crashed; at or beyond the border → left. |
| Border band width | One grid cell, `MapGrid.CellSize` = 146.25 game units. A strict inside/outside test would misreport most normal departures as crashes, because the last marker update before removal usually lands slightly inside the border. |
| Direction basis | 8-point compass from the world centre, binned every 45°. |
| Scope | All map markers — cargo, patrol heli, chinook — in every surface that formats a marker position. |
| Off-map message content | Direction only. No raw coordinates, no "nearest" cell. |
| Wording | "probably crashed at D7" / "left the map to the north-east". |
| Map dimensions unavailable | Fall back to today's behaviour: `HeliLeft` with raw `(x, y)` coordinates. Neither a cell nor a direction is computable without a world size. |

## Design

### Map math (`RustPlusBot.Abstractions/Connections`)

New `MapDirection` enum: `North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest`.

New members on `MapGrid`, which already owns cell size and grid binning:

- `MapDirection DirectionFrom(float x, float y, uint worldSize)` — bearing from the world centre
  (`worldSize / 2` on both axes) to the point, binned into 45° sectors centred on each compass
  point. Recall the world axes: X runs west→east, Y runs south→north. A point exactly at the centre
  is degenerate; return `North` rather than throwing, since it cannot arise for a real off-map
  marker.
- `bool IsOutsideWorld(float x, float y, uint worldSize)` — true when X or Y falls outside
  `[0, worldSize]`.
- `bool IsAtOrBeyondBorder(float x, float y, uint worldSize)` — true when the point is outside the
  world, or within `CellSize` of any edge.

These are pure functions with no localization or grid-style dependency; direction does not vary by
`MapGridStyle`.

### Location description (`RustPlusBot.Features.Events/Formatting`)

New static `MapLocation` alongside the existing `GridReference`:

```csharp
public readonly record struct MapLocationText(bool IsDirection, string Text);

public static MapLocationText Describe(
    ILocalizer localizer, string culture,
    float x, float y, MapDimensions? dims, MapGridStyle style);
```

Behaviour:

- `dims` null or `WorldSize == 0` → `(false, "(x, y)")`, matching `GridReference.From` exactly.
- Outside the world → `(true, <localized direction word>)`.
- Otherwise → `(false, <grid label>)`.

Plus `DescribeDirection(localizer, culture, x, y, dims)` for the departure messages, which always
render a direction regardless of whether the last position was inside the border band. When `dims`
is null it returns `(false, "(x, y)")` like `Describe`, so the caller falls back to the plain
message key and today's raw-coordinate wording.

`IsDirection` is therefore false for both a grid cell and a raw-coordinate fallback: it answers
"does this text name a direction", which is exactly the question the message-key suffix asks.

`GridReference` is unchanged and stays in use by `ServerTeamMessageRenderer` and
`PlayerEventRenderer`.

### Classification (`MarkerEventClassifier`)

New `MapEventKind.HeliCrashed = 5` (appended; the enum is persisted only as in-memory recent-event
state, but appending keeps the existing numbering stable).

Heli marker removal resolves as:

- `evt.Dimensions` is null → `HeliLeft` (unchanged fallback).
- `MapGrid.IsAtOrBeyondBorder(m.X, m.Y, worldSize)` → `HeliLeft`.
- Otherwise → `HeliCrashed`.

Cargo removal stays `CargoLeft`; chinook removal stays silent.

### Rendering

Location rule by message type:

| Message | Location shown |
| --- | --- |
| `HeliCrashed` | Grid cell — inside the world by definition. |
| `HeliLeft`, `CargoLeft` | Direction always (`DescribeDirection`), falling back to raw coordinates only when dimensions are unavailable. |
| Arrivals (`CargoEntered`, `HeliEntered`, `ChinookSpawned`) | `Describe` — grid inside, direction outside. |
| Live position readouts (`!cargo`, `!heli`, `!chinook`, `#info` events embed, `!events` entries) | `Describe`. |

Call sites to update:

- `Features.Events/Rendering/EventEmbedRenderer.cs` — `Render` and `RenderLine`; add the
  `HeliCrashed` arm to both key maps, and append `.dir` to the resolved key when the described
  location is a direction.
- `Features.Commands/Handlers/EventsCommandHandler.cs` — same treatment for the `command.event.*`
  keys.
- `Features.Commands/Handlers/MarkerReply.cs` — `.dir` suffix on `{prefix}.ok`.
- `Features.Events/Messages/ServerEventsMessageRenderer.cs` — swap `GridReference.From` for
  `MapLocation.Describe`. No `.dir` variant: `server.events.out` is a compact
  "Out · {0} · {1} ago" field where a direction word reads correctly on its own — "Out · north-east
  · 3m ago", "Présent · le nord-est · il y a 3m". The French article is slightly redundant there;
  that is the cost of one direction-word set instead of two, and it is confined to this one field.

`MapEventKind` is exhaustively switched in `EventEmbedRenderer` (twice) and `EventsCommandHandler`,
each throwing `ArgumentOutOfRangeException` in the default arm — so a missed `HeliCrashed` arm
surfaces as a test failure rather than a silent fallback.

### Strings

Both `Strings.resx` and `Strings.fr.resx`. `StringsResourceParityTests` already fails the build on
any key present in one file but not the other.

Every message key follows one uniform rule: the renderer appends `.dir` when the described location
is a direction, and uses the plain key otherwise. "Otherwise" covers both a grid cell and the
raw-coordinate fallback, so no existing value changes meaning and the no-dimensions path keeps
today's exact wording. Nothing is repurposed; every direction-worded message is a new `.dir` key.

New direction words. English is bare and pairs with "to the {0}" / "from the {0}" in the message;
French carries its own article so a single message value ("vers {0}", "depuis {0}") works for all
eight, including the two that elide to "l'".

| Key | en | fr |
| --- | --- | --- |
| `direction.n` | north | le nord |
| `direction.ne` | north-east | le nord-est |
| `direction.e` | east | l'est |
| `direction.se` | south-east | le sud-est |
| `direction.s` | south | le sud |
| `direction.sw` | south-west | le sud-ouest |
| `direction.w` | west | l'ouest |
| `direction.nw` | north-west | le nord-ouest |

New crash messages:

| Key | en | fr |
| --- | --- | --- |
| `event.heli.crashed` | 🚁 Patrol Helicopter probably crashed at {0} | 🚁 Hélicoptère de patrouille probablement abattu en {0} |
| `event.heli.crashed.line` | Patrol Helicopter probably crashed at {0} | Hélicoptère de patrouille probablement abattu en {0} |
| `command.event.helicrashed` | heli crashed in {0} | héli abattu en {0} |

New departure `.dir` variants. The existing `event.*.left`, `event.*.left.line` and
`command.event.*left` keys keep their current values verbatim; they now render only on the
no-dimensions fallback path.

| Key | en | fr |
| --- | --- | --- |
| `event.heli.left.dir` | 🚁 Patrol Helicopter left the map to the {0} | 🚁 Hélicoptère de patrouille parti vers {0} |
| `event.heli.left.line.dir` | Patrol Helicopter left the map to the {0} | Hélicoptère de patrouille parti vers {0} |
| `event.cargo.left.dir` | 🚢 Cargo Ship left the map to the {0} | 🚢 Cargo Ship parti vers {0} |
| `event.cargo.left.line.dir` | Cargo Ship left the map to the {0} | Cargo Ship parti vers {0} |
| `command.event.helileft.dir` | heli left to the {0} | héli parti vers {0} |
| `command.event.cargoleft.dir` | cargo left to the {0} | cargo parti vers {0} |

New arrival and live-position `.dir` variants:

| Key | en | fr |
| --- | --- | --- |
| `event.cargo.entered.dir` | 🚢 Cargo Ship entered from the {0} | 🚢 Cargo Ship arrivé depuis {0} |
| `event.cargo.entered.line.dir` | Cargo Ship entered from the {0} | Cargo Ship arrivé depuis {0} |
| `event.heli.entered.dir` | 🚁 Patrol Helicopter entered from the {0} | 🚁 Hélicoptère de patrouille arrivé depuis {0} |
| `event.heli.entered.line.dir` | Patrol Helicopter entered from the {0} | Hélicoptère de patrouille arrivé depuis {0} |
| `event.chinook.spawned.dir` | 🚁 Chinook spawned to the {0} | 🚁 Chinook apparu vers {0} |
| `event.chinook.spawned.line.dir` | Chinook spawned to the {0} | Chinook apparu vers {0} |
| `command.event.cargoentered.dir` | cargo from the {0} | cargo depuis {0} |
| `command.event.helientered.dir` | heli from the {0} | héli depuis {0} |
| `command.event.chinookspawned.dir` | chinook to the {0} | chinook vers {0} |
| `command.cargo.ok.dir` | Cargo Ship to the {0} ({1} ago) | Cargo vers {0} (il y a {1}) |
| `command.heli.ok.dir` | Patrol Helicopter to the {0} ({1} ago) | Hélicoptère vers {0} (il y a {1}) |
| `command.chinook.ok.dir` | Chinook to the {0} ({1} ago) | Chinook vers {0} (il y a {1}) |

Rejected alternative: composing every message from a shared "at D7" / "to the north-east" fragment.
It roughly halves the key count but produces awkward French across differing verbs and forces a
second, bare direction form for the compact `#info` field — so each message keeps its own value.

## Testing

`RustPlusBot.Abstractions.Tests` — `MapGrid`:

- `DirectionFrom` returns the expected compass point for one sample per sector, for points both
  inside and outside the world.
- Sector boundaries: a point due north-east of centre is `NorthEast`; points a hair either side of a
  45° boundary fall in the adjacent sectors.
- `IsOutsideWorld` at exactly 0 and exactly `worldSize` (inside), and just past either (outside).
- `IsAtOrBeyondBorder` at exactly `CellSize` from an edge (inside), just under it (border), and
  outside the world (border).

`RustPlusBot.Features.Events.Tests` — `MarkerEventClassifier`:

- Heli removed at map centre → `HeliCrashed`.
- Heli removed within one cell of an edge → `HeliLeft`.
- Heli removed outside the world → `HeliLeft`.
- Heli removed with null dimensions → `HeliLeft`.
- Cargo removal is still `CargoLeft`; the existing tests must keep passing.

`RustPlusBot.Features.Events.Tests` — `EventEmbedRenderer`:

- `HeliCrashed` renders the crash text with a grid cell, in `en` and `fr`.
- An arrival outside the world renders the `.dir` text with a direction word, not a clamped cell.
- An arrival inside the world still renders the plain key with a cell.
- `HeliLeft` renders the `.dir` text with a direction even when the position is inside the border
  band.
- `HeliLeft` with null dimensions renders the plain key with raw coordinates — today's text,
  unchanged.
- Every asserted string is the resolved value, not the key, so a missing resx entry fails rather
  than silently rendering the key name.

`RustPlusBot.Features.Commands.Tests` — `!events`, `!heli`, `!cargo` replies pick the `.dir` variants
for off-map positions and the plain keys otherwise.

`RustPlusBot.Localization.Tests` — existing parity tests cover the new keys with no changes.

## Risks

- **Band width is a judgement call.** 146.25 units is one grid cell. A heli shot down while hugging
  the map border is reported as having left, and the departure message shows only a direction, so
  that report also omits the cell. This is not a practical loss: the outer band of a Rust map is
  ocean, well outside the land mass, so a helicopter downed there leaves no lootable debris and the
  cell would name water. The opposite error — calling every routine departure a crash — is both more
  likely and more annoying, so the band errs toward "left".
- **Message-key explosion.** ~29 new keys per language. The parity test catches omissions, and the
  exhaustive `switch` arms catch a missed `HeliCrashed` case, so both failure modes are loud.
- **A `.dir` key that is never written is only caught at runtime.** `ILocalizer` resolves an unknown
  key by returning the key itself rather than throwing, so a missing `.dir` variant would surface as
  a literal key in a Discord message. The renderer tests assert the resolved text for both the plain
  and `.dir` paths of every message that gains a variant.
