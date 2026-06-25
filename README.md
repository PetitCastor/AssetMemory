# AssetMemory

A local inventory tracker for Star Citizen. It tails your `Game.log` in the background and remembers where every item ended up, so you can find gear you stashed weeks ago without digging through every container in the 'verse.

## How it works

- A background collector tails `Game.log` (and can re-sync historical backup logs) for inventory move, equip, and container events.
- Events are stored in a local SQLite database as a discovery ledger — locations and quantities are derived purely from what the log reveals.
- Numeric location/player/entity IDs are auto-resolved into readable names (player handle, worn-item name) using data already present in the log — no external API involved.
- A Blazor Server UI serves a searchable, sortable inventory table at `http://localhost:9222`.

## Running

```
dotnet run --project src/AssetMemory.UI
```

On first launch, point it at your Star Citizen install folder (auto-detect or manual path). It starts tracking automatically from then on.

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
