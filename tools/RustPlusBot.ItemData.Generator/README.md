# RustPlusBot.ItemData.Generator

A maintainer CLI that regenerates the embedded Rust item dataset
(`src/RustPlusBot.Features.ItemData/Data/item-data.json`) consumed by the bot's
`/item`, `/recycle`, `/craft`, and `/research` calculators.

This tool is **not** part of the running bot — it is referenced by nothing in
the application graph. It exists so the bundled item data can be refreshed when
Rust updates, rather than shipping a frozen copy that silently rots.

## Why it exists

The reference data has two very different freshness profiles:

- **Item names / ids** are well maintained and current.
- **Calculator data** (recycle yields, craft recipes, research costs) is sourced
  from rustlabs and tends to lag game updates.

So the dataset carries per-section provenance dates (`Sources.NamesAsOf`,
`RecycleAsOf`, `CraftAsOf`, `ResearchAsOf`), surfaced to users as a
"data as of `<date>`" footer, and this tool can re-emit a fresh snapshot on
demand. It **validates loudly and refuses to overwrite a good bundle with
garbage** if the upstream shape drifts.

## What it does

1. Reads the [rustplusplus](https://github.com/alexemanuelol/rustplusplus) static
   data files (offline transform — no network):

   | File | Provides |
   | --- | --- |
   | `items.json` | id → display name (the item spine) |
   | `rustlabsStackData.json` | stack size |
   | `rustlabsDespawnData.json` | despawn time |
   | `rustlabsRecycleData.json` | recycler yields |
   | `rustlabsCraftData.json` | craft ingredients + time |
   | `rustlabsResearchData.json` | research scrap cost |

2. Projects them into our own typed schema (`ItemDataset` /
   `ItemRecord` / …, defined in `RustPlusBot.Features.ItemData`), keyed by item id,
   with calculator data inlined per item (null where an item has none).
3. **Validates** the result (`DatasetValidator`): a minimum item-count floor and
   that every recycle-yield / craft-ingredient id resolves to a known item.
4. On any validation error, prints the errors to stderr and exits non-zero
   **without writing** — the existing good bundle is never clobbered.
5. On success, writes the dataset as indented JSON and exits 0.

Items present in a rustlabs file but absent from `items.json` (no name) are
dropped, and the dropped counts are logged — never silent.

## Usage

```bash
dotnet run --project tools/RustPlusBot.ItemData.Generator -- \
  --out src/RustPlusBot.Features.ItemData/Data/item-data.json \
  --rustplusplus ~/Dev/rustplusplus/src/staticFiles \
  --min-items 1000
```

| Argument | Required | Default | Meaning |
| --- | --- | --- | --- |
| `--out <path>` | yes | — | Where to write `item-data.json` |
| `--rustplusplus <dir>` | no | `~/Dev/rustplusplus/src/staticFiles` | Directory holding the source JSON files |
| `--min-items <n>` | no | `1000` | Validation floor; fewer items than this fails the run |

Exit code `0` on success, `1` on a usage error or validation failure.

After regenerating, rebuild and run the test suite — the runtime loader
(`EmbeddedItemDatabase`) deserializes this file at startup, and the `ItemData`
tests assert known items resolve correctly.

## Refreshing the dataset

1. Update your local rustplusplus checkout so its `src/staticFiles` is current.
2. Run the command above.
3. If validation fails, the bundle is left untouched — investigate the reported
   drift (an upstream file that changed shape, or a count below the floor)
   before re-running.
4. Bump the relevant `Sources.*AsOf` dates in
   [`Program.cs`](Program.cs) to reflect the new source dates, re-run, then
   commit the regenerated `item-data.json`.

## Provenance dates

The source dates stamped into the dataset are currently hard-coded constants in
[`Program.cs`](Program.cs) (`NamesAsOf`, `RecycleAsOf`, `CraftAsOf`,
`ResearchAsOf`). Update them when you refresh from newer upstream data.

## Notes

- 6a ships the **offline transform** only. A live-scrape source adapter
  (`IRustLabsSource`) is sketched as a seam for a future slice; for now the tool
  transforms a local rustplusplus checkout.
- Recycle data covers the standard **recycler** only; safe-zone recycler and
  shredder yields are out of scope for this slice.

## Attribution

Item names/ids originate from Facepunch's published item list; recycle/craft/
research data originates from [rustlabs](https://rustlabs.com/) via the
rustplusplus static files.
