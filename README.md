<img src="src/AssetMemory/Resources/app-icon.ico" width="96" alt="AssetMemory icon" />

# AssetMemory

**Every station. Every scu box. Remembered.** — a local inventory tracker for Star Citizen. It watches your `Game.log` in the background and remembers where every item ended up, so you stop opening forty-seven containers across three systems looking for one (1) bottle of Synergy.

## Features

- **Automatic tracking** — tails `Game.log` live; every move, equip, drop, store, and container open is recorded. No manual entry.
- **Bulk "Move all"** — batched multi-item moves (backpack ⇄ station ⇄ box) are captured, not just single drags.
- **Everything nests where it sits** — backpacks, SCU boxes, and station lockers roll up under the station/place they're at. Move an item into a box and it drops one level deeper, never disappears.
- **Search everywhere at once** — across every location, with SQL wildcards: `gun%` (starts with), `%rifle` (ends with), `_` (one character).
- **Cascading filters** — System → Place → Container (e.g. Nyx → Castra Jump Point → a Stor-All box), with a live breadcrumb.
- **Readable names** — numeric IDs resolved to item / station / player names from the log, your game's `global.ini`, and (fallback, once, cached) [api.star-citizen.wiki](https://star-citizen.wiki).
- **Track-from date** — ingests all history (incl. rotated backups) by default; set a start date to only track forward. Empty date = all.
- **Clear database** — one-click wipe to start fresh.
- **Log-derived, nothing guessed** — locations and quantities come purely from what the log has revealed, so a restart resumes exactly where it left off.
- **System-tray app** — no console, no forced browser tab; UI at `http://localhost:9222`, single instance.
- **Single self-contained exe** — no .NET runtime to install, no telemetry.

## Download & run

1. Unzip `AssetMemory-win-x64.zip` — one file, `AssetMemory.exe`.
2. Run it. It starts in the system tray; right-click → **Open** (or double-click) for the UI.
3. First run only: point it at your Star Citizen folder (auto-detect usually finds it).

Tracks automatically from then on. Data lives in `%LOCALAPPDATA%\AssetMemory` (never next to the exe), so replacing the install never touches it. Unsigned → SmartScreen may warn on first run: "More info" → "Run anyway".

## Build from source

```
./publish.ps1                          # -> dist/AssetMemory-win-x64.zip
dotnet run --project src/AssetMemory   # run from source
dotnet test                            # run the test suite
```

- `src/AssetMemory` — Blazor Server UI + tray host
- `src/AssetMemory.Core` — log parsing, inventory events, name/location resolution
- `src/AssetMemory.Data` — SQLite store + event application
- `src/AssetMemory.Collector` — log tailing + collector background service
- `tests/` — one test project per `src/` project, using real captured log fixtures

## Attribution

Item names not found in your game's `global.ini` are looked up against [api.star-citizen.wiki](https://star-citizen.wiki), credited here per its terms of use.
