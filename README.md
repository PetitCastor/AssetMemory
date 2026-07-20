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
- Events are stored in a local SQLite database as a discovery ledger — locations and quantities are derived purely from what the log reveals. Containers (Stor-All boxes, etc.) nest under the station/place they sit at, so moving an item into a box doesn't make it disappear from view. No item left un-spreadsheeted.
- Numeric location/player/entity IDs are auto-resolved into readable names (player handle, worn-item name, "New Babbage" instead of `3170699229`) using data already present in the log or your game's `global.ini` — no telemetry, no analytics, nothing sent anywhere by default. The one exception: item classes with no local name are looked up once against `api.star-citizen.wiki` (rate-limited, cached locally so it's never repeated) — see [Attribution](#attribution).
- You can set a "track from" date to ignore everything before it, and rebuild your history on demand — both persist across restarts.
- A Blazor Server UI serves a searchable, sortable inventory table at `http://localhost:9222`, complete with pagination, because apparently some of us own 300+ distinct items and a flat unpaginated list was a war crime.

## Running

```
dotnet run --project src/AssetMemory
```

On first launch, point it at your Star Citizen install folder (auto-detect or manual path). It starts tracking automatically from then on — no further effort required from you, which, statistically, is the part of this project you'll appreciate most.

## Project layout

- `src/AssetMemory` — Blazor Server UI + tray host, the app itself
- `src/AssetMemory.Core` — log parsing, inventory events, name/location resolution
- `src/AssetMemory.Data` — SQLite store and event application
- `src/AssetMemory.Collector` — log tailing and the collector background service
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

This produces `dist/AssetMemory-win-x64.zip` (~56 MB). To share it: send that zip; the recipient unzips it and runs `AssetMemory.exe` — that's the whole install, nothing else needed alongside it. Its data (`settings.json`, `assetmemory.db`) lives in `%LOCALAPPDATA%\AssetMemory`, not next to the exe, so redeploying or moving the install never touches it.

Notes:
- `./publish.ps1 -NoZip` produces just the publish folder without archiving.
- It's a genuine single-file exe — every static asset (CSS, the vendored `blazor.web.js`, the app icon) is embedded in the assembly, not shipped as loose files next to it.
- The exe is unsigned, so Windows SmartScreen may warn on first run ("More info" → "Run anyway"). Code-signing is a future step.

## Attribution

Item names not found in your game's `global.ini` are looked up against [api.star-citizen.wiki](https://star-citizen.wiki), credited here per its terms of use.

---

*No items were harmed in the making of this tracker. Several were, however, finally located.*
