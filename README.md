<img src="src/AssetMemory/Resources/app-icon.ico" width="96" alt="AssetMemory icon" />

# AssetMemory

**Asset Memory — We remember for you.**

A local inventory tracker for Star Citizen. It watches your `Game.log` in the background and remembers where every item ended up, so you stop opening forty-seven containers across three star systems looking for one (1) bottle of Synergy.

## What it offers

- A searchable, sortable, paginated inventory table served locally at `http://localhost:9222`. The search box supports SQL-style wildcards — `gun%` for "starts with", `%rifle` for "ends with", `_` for exactly one character — on top of the default "contains" match.
- Filtering that cascades **System → Place → Container** (e.g. Nyx → Nyx Castra Jump Point → a Stor-All box), with a breadcrumb showing exactly what's in view.
- Runs quietly in the **system tray** — no console window, no browser tab forced open. Right-click for Open/Exit; launching it twice just reopens the UI instead of starting a duplicate.
- Ships as a single self-contained executable. No .NET runtime to install, no companion files to lose track of.

## How it tracks your inventory

AssetMemory tails `Game.log` the same way the game itself writes to it — live, in the background. Every item move, equip, drop, and container open/close the log records gets parsed into a typed event and applied to a local SQLite database. That includes the actions the log disguises as something other than a plain move: equipping an item straight out of a storage box (or off a station locker) and storing one straight back in are tracked too, so a box's contents stay correct whether you drag items in and out or equip and stow them directly.

That database is a **discovery ledger**, not a snapshot: locations and quantities are derived purely from what the log has revealed, event by event. Nothing is guessed. If you've never opened a container, AssetMemory doesn't know it exists; the moment you do, it starts tracking everything that happens to it. Containers (Stor-All boxes, etc.) nest under the station or place they physically sit at, so moving an item into a box doesn't make it disappear from view — it just shows up one level deeper.

Numeric location/player/entity IDs from the log get resolved into readable names (a player handle, a worn item's name, "New Babbage" instead of `3170699229`) using data already present in the log or your game's `global.ini`. No telemetry, no analytics, nothing sent anywhere by default — the one exception is the wiki lookup below.

## Tracking from a starting date

By default AssetMemory ingests your entire log history, including rotated backup logs, from the very first session it can find. If you'd rather it only remember what happens from a certain point forward, set a **"track from"** date in the UI: everything before that date is dropped, and the database is rebuilt from the remaining events.

That bound persists across restarts — set it once and every future sync (manual or automatic) respects it. Change your mind later and hit **"Ingest all"** to go back to full history; either action triggers a full rebuild from the log, so it fully replaces the tracked data rather than merging two views of it. A rebuild replays your logs — rotated backups and the current one — in chronological order, so an item you took out of a box in a later session doesn't get resurrected by an older backup that still remembers it there.

## The star-citizen.wiki hook

Most item names come for free — either straight from the log or from your game's own `global.ini` localization file. For the items that have neither (newer or less common gear), AssetMemory looks the class name up **once** against [api.star-citizen.wiki](https://star-citizen.wiki), caches the result locally, and never asks again for that item. Lookups are rate-limited and run in the background at startup — they never block the UI, and a restart won't re-trigger lookups already resolved.

## Download & run

1. Grab `AssetMemory-win-x64.zip` and unzip it anywhere — there's just the one file inside, `AssetMemory.exe`.
2. Run it. It starts hidden in the system tray; right-click the tray icon and pick **Open** (or just double-click it) to open the UI in your browser.
3. First time only: point it at your Star Citizen install folder (auto-detect usually finds it, or enter the path manually).
4. That's it. It tracks automatically from then on — open your inventory in-game and refresh the page to see it show up.

Don't have a prebuilt zip? Build one yourself from the repo root:

```
./publish.ps1
```

This produces `dist/AssetMemory-win-x64.zip`. Your data (`settings.json`, `assetmemory.db`) lives in `%LOCALAPPDATA%\AssetMemory`, never next to the exe, so moving or replacing the install never touches it. The exe is unsigned, so Windows SmartScreen may warn on first run — "More info" → "Run anyway".

## Project layout

- `src/AssetMemory` — Blazor Server UI + tray host, the app itself
- `src/AssetMemory.Core` — log parsing, inventory events, name/location resolution
- `src/AssetMemory.Data` — SQLite store and event application
- `src/AssetMemory.Collector` — log tailing and the collector background service
- `tests/` — matching test project per `src/` project, using real captured log fixtures

```
dotnet run --project src/AssetMemory   # run from source
dotnet test                            # run the test suite
```

## Attribution

Item names not found in your game's `global.ini` are looked up against [api.star-citizen.wiki](https://star-citizen.wiki), credited here per its terms of use.
