# Task: Item display names don't match in-game names

**Status: resolved** (2026-07-19)

## Goal
Investigate and fix cases where AssetMemory shows an item name that differs from what Star Citizen shows in-game.

## Findings

Confirmed case: `class_name = grin_multitool_resource_salvage_repair_01_filled`, shown by AssetMemory
as the heuristic-formatted `"Grin multitool resource salvage repair 01 filled"`; the real in-game name
is **"Cambio-Lite SRT Canister"**.

### Root cause

`Game.log` genuinely emits `ItemClass[grin_multitool_resource_salvage_repair_01_filled]`, but the
user's `global.ini` has **no** `item_Name` key for that exact string — the localization key for this
item uses a differently-worded class name entirely (`item_Namegrin_multitool_01_salvage_mag_filled,
P=Cambio-Lite SRT Canister (Filled)`). The Game.log `ItemClass` token and the item's `global.ini`
localization key are simply two different identifiers for the same item — not a parsing bug in
`GameItemNames` (`src/AssetMemory.Core/Resolution/GameItemNames.cs`). No amount of smarter ini lookup
can bridge that; it needs an external item catalog that can resolve the log's own class string.

### scmdb.net — ruled out

`Z:\Projects\Star Citizen\ODB\CraftingLookup.cs` caches scmdb.net's crafting-blueprint feed. Tested
directly: downloaded both scmdb feeds (`crafting_blueprints-4.9.0-live.12248363.json`,
`merged-4.9.0-live.12248363.json`) and grepped for `cambio`/`grin_` — zero hits in either. Confirmed
via the site's own metadata and JS bundle: scmdb.net only indexes missions, crafting blueprints, and
mining/salvage pools — no general item/gear catalog. Cambio-Lite SRT Canister is a purchasable
multitool attachment, not a craftable product or mission reward, so it's structurally out of scope
(likely true for most gear/weapon/attachment mismatches, not just this one).

### star-citizen.wiki API — confirmed working, adopted

`https://api.star-citizen.wiki/api/search/{class_name}` (unauthenticated, `Accept: application/json`):
- `grin_multitool_resource_salvage_repair_01_filled` → 302 → `name: "Cambio-Lite SRT Canister"` (exact
  match on the log's own `class_name` field).
- Sanity check on an already-working ini class, `behr_smg_ballistic_01` → 302 → "P8-SC SMG" (matches
  global.ini, consistent).
- Garbage class name → clean 404 JSON (`{"message":"No matching entity found."}`).
- No API key required; search rate-limited to 60 requests/min/IP; must credit "api.star-citizen.wiki"
  in public projects; non-commercial use only (fine for AssetMemory).

## Fix

Added a one-shot background backfill (`ExternalItemNameBackfillService` +
`ExternalItemNameClient` in `src/AssetMemory.Collector/`) that fills in names global.ini can't
resolve, using star-citizen.wiki's exact `class_name` search:
- Local `global.ini` stays the primary/authoritative source (instant, offline, no rate limit) — the
  API only gets called for items `IItemNameResolver.HasOverride` says it missed.
- Heuristic formatting remains the final fallback when both miss (any API failure — offline, API
  down, malformed response, no match — is non-fatal and just keeps the current name).
- Runs as a `BackgroundService`, registered in both `Program.cs` (web) and `AppHost.cs` (TUI),
  starting automatically alongside the existing hosted services without blocking app startup.
- 1s delay between lookups keeps it well under the 60 req/min rate limit.
- Attribution added: "Item data: api.star-citizen.wiki" credit line in `Home.razor` (web footer) and
  `InventoryWindow.cs` (TUI status line).

## Acceptance

- [x] Root cause documented (ini key ≠ log's ItemClass token, not a parser bug).
- [x] scmdb.net ruled out and why documented.
- [x] star-citizen.wiki API confirmed working and adopted as the external name source.
- [x] Fix implemented, tested (`ExternalItemNameClientTests`, `ExternalItemNameBackfillServiceTests`),
      and attribution added per API terms.
