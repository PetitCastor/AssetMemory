# Task: Start Fresh re-hides labels

## Goal
Fix `GameLogCollector.StartFresh` so place/container labels survive a Start Fresh, instead of going unlabelled until the player re-opens them in-game.

## Context
`src/AssetMemory.Collector/GameLogCollector.cs:38-45`:

```csharp
public void StartFresh(Action clearData)
{
    lock (_lock)
    {
        clearData();
        _tailer.SeekToEnd();
    }
}
```

`clearData()` wipes the DB, then `_tailer.SeekToEnd()` jumps straight past any already-streamed `RequestLocationInventory` / `OpenNestedInventory` identification lines. Those lines are what give a place/container its label (see `NestedContainerParser` and related event parsers in `src/AssetMemory.Core/Inventory/Events/EventParsers.cs`). Since the tailer never sees them again, a station/box the player doesn't manually re-open after Start Fresh stays unlabelled (and its held items stay hidden per current rollup rules) until the game re-emits the identification line.

## Approach
1. Before (or instead of) `SeekToEnd()`, replay the log from the start (or from wherever it's already been read) applying only identification-type events (station/container identity), not holding/move events — so labels are re-seeded without re-counting inventory.
2. Then seek to end so subsequent holding events pick up fresh from "now" as Start Fresh intends.
3. Needs a way to filter "identity-only" events out of `InventoryLogReader`'s output, or a dedicated identity-replay pass — check `EventApplier`/`EventParsers` for how identity vs. holding events are currently distinguished (e.g. `ContainerIdentifiedEvent` vs. move/quantity events).

## Acceptance
- After Start Fresh, previously-identified places/containers keep their labels without requiring the player to re-open them in-game.
- Held-item counts still reset to zero on Start Fresh (only labels are re-seeded, not holdings).
