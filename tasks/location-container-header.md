# Task: Location/container header breadcrumb

## Goal
Show the current selection (place, and container if drilled in) as a header/breadcrumb above the inventory table, in both the web UI and the TUI.

## Context
`feature/containers` (merged to `main`) added a place dropdown and a conditional container dropdown, plus rollup semantics in `AssetMemoryStore.GetHoldingDetailsPage(placeId, containerId, ...)`:
- container selected → just that box
- place selected, no container → place-direct holdings + all child boxes rolled up
- neither selected → everything

There's currently no visual summary of what's being viewed beyond the dropdown values themselves.

## Relevant code
- `src/AssetMemory.UI/Components/Pages/Home.razor` — `SelectedPlace` (line 295), `SelectedContainer` (line 302), used to build `placeId`/`containerId` around line 498-506 before calling `Store.GetHoldingDetailsPage`.
- `src/AssetMemory.Tui/Ui/InventoryWindow.cs` — mirrors the same place/container selection for the TUI.
- `src/AssetMemory.UI/wwwroot/app.css` — styling home for the new header element (see `.inception-bar` for an example of an existing small status bar).

## Approach
1. Web: add a breadcrumb line above the table, e.g. "All locations" / "{Place}" / "{Place} › {Container}", built from the resolved place/container names (not just IDs) — need to look up display names from `Places`/`Containers` collections already loaded for the dropdowns.
2. TUI: same breadcrumb as a `Label` above `_table` in `InventoryWindow`, updated in `Reload()` alongside `_watchingLabel`.
3. Keep it reactive — update whenever `SelectedPlace`/`SelectedContainer` changes and `Reload()`/`LoadData()` runs.

## Acceptance
- Header text matches current dropdown selection at all times.
- Reflects the three rollup states correctly (all / place+children / container-only).
- No regression to existing dropdown behavior.
