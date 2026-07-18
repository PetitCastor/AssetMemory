```
   ╭─────╮
   │     │
   │     │
   ╰─────╯
         ╲
          ╲
           ╲
```

# AssetMemory

*"Where the heck did I put my medpens?" — every Star Citizen player, every single play session*

A local inventory tracker for Star Citizen. It tails your `Game.log` in the background like a small, well-behaved stalker, and remembers where every item ended up — so you can stop opening forty-seven containers across three star systems looking for one (1) bottle of Synergy.

It cannot find your ship. It cannot find your sanity after a server crash ate your loot run. It can, however, tell you with great confidence that the rifle you swear you picked up is, in fact, sitting in a box in New Babbage where you left it eleven days ago.

## How it works

- A background collector tails `Game.log` (and can re-sync historical backup logs) for inventory move, equip, and container events — silently, in the background, judging none of your hoarding habits.
- Events are stored in a local SQLite database as a discovery ledger — locations and quantities are derived purely from what the log reveals. No item left un-spreadsheeted.
- Numeric location/player/entity IDs are auto-resolved into readable names (player handle, worn-item name, "New Babbage" instead of `3170699229`) using data already present in the log — no external API, no telemetry, no cloud. Your hoarding is for your eyes only.
- A Blazor Server UI serves a searchable, sortable inventory table at `http://localhost:9222`, complete with pagination, because apparently some of us own 300+ distinct items and a flat unpaginated list was a war crime.

## Running

```
dotnet run --project src/AssetMemory.UI
```

On first launch, point it at your Star Citizen install folder (auto-detect or manual path). It starts tracking automatically from then on — no further effort required from you, which, statistically, is the part of this project you'll appreciate most.

## Project layout

- `src/AssetMemory.Core` — log parsing, inventory events, name/location resolution
- `src/AssetMemory.Data` — SQLite store and event application
- `src/AssetMemory.Collector` — log tailing and the collector background service
- `src/AssetMemory.UI` — Blazor Server UI
- `tests/` — matching test project per `src/` project, using real captured log fixtures

## Tests

```
dotnet test
```

## Packaging & distribution

The app ships as a single self-contained Windows executable — no .NET runtime needs to be installed on the target machine. When launched it runs in the **system tray** (no console window), opens the inventory UI in your default browser, and keeps collecting in the background. Right-click the tray icon for **Open** / **Exit**; launching it a second time just re-opens the UI rather than starting a duplicate.

Build the distributable from the repo root:

```
./publish.ps1
```

This produces `dist/AssetMemory-win-x64.zip` (~56 MB). To share it: send that zip; the recipient unzips it anywhere **writable** (not `Program Files`, since the app writes `settings.json` and `assetmemory.db` next to the exe) and runs `AssetMemory.exe`.

Notes:
- `./publish.ps1 -NoZip` produces just the publish folder without archiving.
- The whole unzipped folder is required, not only the exe — `wwwroot/` and the static-asset manifest sit alongside it.
- The exe is unsigned, so Windows SmartScreen may warn on first run ("More info" → "Run anyway"). Code-signing is a future step.
- Drop an `app.ico` next to the exe to replace the default tray icon — it's picked up automatically.

## Console (TUI) edition

`src/AssetMemory.Tui` is an alternative, **terminal** front-end (Terminal.Gui) over the same
Core/Data/Collector layer — a live, filterable, sortable, paged inventory table with no browser and
no web stack. Because it has no `wwwroot`, the published build is a genuine **single-file** exe.

```
dotnet run --project src/AssetMemory.Tui
./publish-tui.ps1                 # -> dist/AssetMemory-Tui-win-x64.zip (single AssetMemory.Tui.exe)
```

It picks its mode automatically via the shared single-instance lock:
- **Standalone** (no other AssetMemory running) — it hosts the collector itself and owns its data.
- **Viewer** (a background AssetMemory/tray app is already running) — it opens that instance's database
  read-only for live viewing and delegates writes (sync / start-fresh / change-folder) to the running
  host over its localhost control API (`/api/info`, `/api/sync`, `/api/clear`, `/api/path`).

Best experience in Windows Terminal (mouse + 24-bit color); works in legacy conhost too.

---

*No items were harmed in the making of this tracker. Several were, however, finally located.*
