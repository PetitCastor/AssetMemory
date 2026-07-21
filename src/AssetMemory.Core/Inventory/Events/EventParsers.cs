using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using AssetMemory.Core.Logs;

namespace AssetMemory.Core.Inventory.Events;

/// <summary>Turns a single <see cref="LogEntry"/> into one typed <see cref="InventoryEvent"/>, if it recognises it.</summary>
public interface IInventoryEventParser
{
    bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev);
}

internal static class FieldHelpers
{
    private static readonly Regex ForPlayerRegex = new(@"for '([^']+)'", RegexOptions.Compiled);

    /// <summary>Player handle from a "Queued Request … for 'Name'" line.</summary>
    public static string? PlayerFromFor(string message)
    {
        var m = ForPlayerRegex.Match(message);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static bool TryInt(string? text, out int value)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static bool TryLong(string? text, out long value)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}

/// <summary>Parses a committed "Queued Request … Type[OpenNestedInventory]" line.</summary>
public sealed class ContainerOpenedParser : IInventoryEventParser
{
    public bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev)
    {
        ev = null;
        if (entry.Category != "InventoryManagementRequest"
            || !entry.Message.StartsWith("Queued Request", StringComparison.Ordinal)
            || LogFields.Get(entry.Message, "Type") != "OpenNestedInventory")
            return false;

        if (!InventoryRef.TryParse(LogFields.Get(entry.Message, "Source Inventory"), out var container))
            return false;
        if (!FieldHelpers.TryInt(LogFields.Get(entry.Message, "Request"), out var requestId))
            return false;

        ev = new ContainerOpenedEvent(
            entry.Timestamp,
            FieldHelpers.PlayerFromFor(entry.Message) ?? "",
            container,
            LogFields.Get(entry.Message, "Source") ?? "",
            requestId);
        return true;
    }
}

/// <summary>Parses a committed "Queued Request … Type[Move]" line — the source of item identity + quantity.</summary>
public sealed class MoveEventParser : IInventoryEventParser
{
    public bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev)
    {
        ev = null;
        if (entry.Category != "InventoryManagementRequest"
            || !entry.Message.StartsWith("Queued Request", StringComparison.Ordinal)
            || LogFields.Get(entry.Message, "Type") != "Move")
            return false;

        var itemClass = LogFields.Get(entry.Message, "Source");
        if (string.IsNullOrEmpty(itemClass))
            return false;
        if (!FieldHelpers.TryInt(LogFields.Get(entry.Message, "amount"), out var quantity))
            return false;
        if (!InventoryRef.TryParse(LogFields.Get(entry.Message, "Source Inventory"), out var source))
            return false;
        InventoryRef.TryParse(LogFields.Get(entry.Message, "Target Inventory"), out var target);
        if (!FieldHelpers.TryInt(LogFields.Get(entry.Message, "Request"), out var requestId))
            return false;

        ev = new ItemMovedEvent(
            entry.Timestamp,
            FieldHelpers.PlayerFromFor(entry.Message) ?? "",
            itemClass,
            quantity,
            source,
            target,
            requestId);
        return true;
    }
}

/// <summary>
/// Stateful: recognises an item dropped out of an inventory into the physical world (onto the
/// ground, a freight elevator platform, etc.) rather than moved to another inventory. The game
/// splits this across three lines with a drifting <c>Type</c> tag, all sharing one request id:
/// <list type="number">
/// <item>
/// <c>&lt;Add Inventory Management Move&gt; New request[N] ... Type[Drop] SourceInventory[...]
/// ItemClass[...]</c> -- the only line carrying <c>Type[Drop]</c>, so it is the gate. Carries no
/// quantity. Note the lowercase <c>request[N]</c> here (unlike every other request-id field).
/// </item>
/// <item>
/// <c>&lt;InventoryManagementRequest&gt; Queued Request[N] Type[Interaction] ... Source[class]
/// amount[n]</c> -- the <c>Type</c> has already drifted to <c>Interaction</c> by this line, so it
/// cannot gate on its own; matched back to the pending drop by request id. Supplies quantity.
/// </item>
/// <item>
/// <c>&lt;UnstowPendingEntities&gt; Unstow Request[N] ... finalized spawn of '...' [entityId], M
/// remaining</c> -- reports the world entity the dropped item became. Emits the event.
/// </item>
/// </list>
/// Lines are processed in log order on a single thread; a dropped request that never reaches the
/// third line (e.g. the drop failed) simply leaves the pending state to be overwritten by the next one.
/// </summary>
public sealed partial class DropEventParser : IInventoryEventParser
{
    private static readonly TimeSpan PairWindow = TimeSpan.FromSeconds(2);

    [GeneratedRegex(@"finalized spawn of '[^']*'\s*\[(\d+)\]", RegexOptions.Compiled)]
    private static partial Regex SpawnedEntityRegex();

    private int _pendingRequestId;
    private string? _pendingItemClass;
    private InventoryRef _pendingSource;
    private string? _pendingPlayer;
    private int _pendingQuantity;
    private DateTimeOffset _pendingAt;

    public bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev)
    {
        ev = null;

        if (entry.Category == "Add Inventory Management Move"
            && entry.Message.StartsWith("New request", StringComparison.Ordinal)
            && LogFields.Get(entry.Message, "Type") == "Drop")
        {
            var itemClass = LogFields.Get(entry.Message, "ItemClass");
            if (!string.IsNullOrEmpty(itemClass)
                && FieldHelpers.TryInt(LogFields.Get(entry.Message, "request"), out var reqId)
                && InventoryRef.TryParse(LogFields.Get(entry.Message, "SourceInventory"), out var source))
            {
                _pendingRequestId = reqId;
                _pendingItemClass = itemClass;
                _pendingSource = source;
                _pendingPlayer = null;
                _pendingQuantity = 1; // fallback if the queued line below never turns up
                _pendingAt = entry.Timestamp;
            }
            return false;
        }

        if (_pendingItemClass is null || entry.Timestamp - _pendingAt > PairWindow)
            return false;

        if (entry.Category == "InventoryManagementRequest"
            && entry.Message.StartsWith("Queued Request", StringComparison.Ordinal)
            && FieldHelpers.TryInt(LogFields.Get(entry.Message, "Request"), out var queuedId)
            && queuedId == _pendingRequestId)
        {
            _pendingPlayer = FieldHelpers.PlayerFromFor(entry.Message);
            if (FieldHelpers.TryInt(LogFields.Get(entry.Message, "amount"), out var qty))
                _pendingQuantity = qty;
            return false;
        }

        if (entry.Category == "UnstowPendingEntities"
            && FieldHelpers.TryInt(LogFields.Get(entry.Message, "Request"), out var unstowId)
            && unstowId == _pendingRequestId)
        {
            var m = SpawnedEntityRegex().Match(entry.Message);
            if (!m.Success || !FieldHelpers.TryLong(m.Groups[1].Value, out var entityId))
            {
                _pendingItemClass = null;
                return false;
            }

            ev = new ItemDroppedEvent(
                entry.Timestamp,
                _pendingPlayer ?? "",
                _pendingItemClass,
                _pendingQuantity,
                _pendingSource,
                entityId,
                _pendingRequestId);
            _pendingItemClass = null;
            return true;
        }

        return false;
    }
}

/// <summary>Parses a "&lt;GetGridItem&gt; … Number of Items[N] in Inventories[M]" line.</summary>
public sealed class GridItemCountParser : IInventoryEventParser
{
    public bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev)
    {
        ev = null;
        if (entry.Category != "GetGridItem"
            || !FieldHelpers.TryInt(LogFields.Get(entry.Message, "Number of Items"), out var stacks))
            return false;

        FieldHelpers.TryInt(LogFields.Get(entry.Message, "Request"), out var requestId);
        FieldHelpers.TryInt(LogFields.Get(entry.Message, "Inventories"), out var inventoryCount);

        ev = new GridItemCountEvent(entry.Timestamp, requestId, stacks, inventoryCount);
        return true;
    }
}

/// <summary>Parses an "&lt;AttachmentReceived&gt;" line into an equipped-loadout item.</summary>
public sealed class EquippedItemParser : IInventoryEventParser
{
    public bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev)
    {
        ev = null;
        if (entry.Category != "AttachmentReceived")
            return false;

        var attachment = LogFields.Get(entry.Message, "Attachment");
        if (string.IsNullOrEmpty(attachment))
            return false;

        // "instanceName, item_class, entityId"
        var parts = attachment.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !FieldHelpers.TryLong(parts[2], out var entityId))
            return false;

        ev = new EquippedItemEvent(
            entry.Timestamp,
            LogFields.Get(entry.Message, "Player") ?? "",
            parts[1],
            parts[0],
            entityId,
            LogFields.Get(entry.Message, "Port") ?? "",
            LogFields.Get(entry.Message, "Status") ?? "");
        return true;
    }
}

/// <summary>Parses a "&lt;Remove Inventory Container UI&gt; … inventory [ref] removed from UI" line.</summary>
public sealed partial class ContainerClosedParser : IInventoryEventParser
{
    [GeneratedRegex(@"inventory \[([^\]]+)\] removed", RegexOptions.Compiled)]
    private static partial Regex ClosedRefRegex();

    public bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev)
    {
        ev = null;
        if (entry.Category != "Remove Inventory Container UI")
            return false;

        var m = ClosedRefRegex().Match(entry.Message);
        if (!m.Success || !InventoryRef.TryParse(m.Groups[1].Value, out var container))
            return false;

        ev = new ContainerClosedEvent(
            entry.Timestamp,
            LogFields.Get(entry.Message, "Player") ?? "",
            container);
        return true;
    }
}

/// <summary>
/// Stateful: recognises a station's persistent local inventory by pairing a
/// <c>&lt;RequestLocationInventory&gt;</c> line (carries the readable code) with the
/// numeric-ref line that immediately follows it (carries the <c>GEID:Location:placeId</c> ref).
/// The first line stashes state and reports no event; the second emits a
/// <see cref="StationIdentifiedEvent"/>.
///
/// The follow-up line's category has drifted across game versions: older logs tag it
/// <c>&lt;RequestInventory&gt;</c>, current builds tag it <c>&lt;Query Inventory&gt;</c>. Both are
/// accepted. This is the gate that separates stations from the many container opens that share
/// those categories: a bare line with no pending location-request is ignored, and only a
/// <c>:Location:</c> ref qualifies. Lines are processed in log order on a single thread.
/// </summary>
public sealed class StationInventoryParser : IInventoryEventParser
{
    // The game renamed this log category from "RequestInventory" to "Query Inventory"; accept both
    // so identification keeps working across builds.
    private static readonly string[] NumericRefCategories = ["RequestInventory", "Query Inventory"];

    // The two lines are always adjacent and share a timestamp to within ~1ms; anything beyond
    // this window means the pending request was dangling (e.g. across a file/tick boundary).
    private static readonly TimeSpan PairWindow = TimeSpan.FromSeconds(1);

    private string? _pendingCode;
    private string? _pendingPlayer;
    private DateTimeOffset _pendingAt;

    public bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev)
    {
        ev = null;

        if (entry.Category == "RequestLocationInventory")
        {
            var code = LogFields.Get(entry.Message, "Location");
            if (!string.IsNullOrEmpty(code))
            {
                _pendingCode = code;
                _pendingPlayer = LogFields.Get(entry.Message, "Player") ?? "";
                _pendingAt = entry.Timestamp;
            }
            return false; // state stashed; the event comes on the paired line.
        }

        if (Array.IndexOf(NumericRefCategories, entry.Category) < 0)
            return false;

        if (_pendingCode is null || entry.Timestamp - _pendingAt > PairWindow)
            return false;

        var pendingCode = _pendingCode;
        var pendingPlayer = _pendingPlayer ?? "";
        _pendingCode = null;
        _pendingPlayer = null;

        if (!InventoryRef.TryParse(LogFields.Get(entry.Message, "Inventory"), out var inv)
            || inv.Kind != InventoryKind.Location)
            return false;

        ev = new StationIdentifiedEvent(entry.Timestamp, pendingPlayer, inv.Id, pendingCode);
        return true;
    }
}

/// <summary>
/// Stateful: recognises the loading-platform (ship / freight elevator) activity that betrays which
/// station the player is physically at -- well before, or entirely without, the Local Inventory
/// panel a <see cref="StationInventoryParser"/> depends on. The platform-manager name carries a
/// trailing location token: a landing-zone place (<c>..._Manager_Levski</c>) or a bare system on a
/// hangar elevator (<c>..._HangarMedium_Nyx</c>). Emits only when a platform reaches an
/// <c>Open…</c> state (the elevator the player is actually using), and only when the token differs
/// from the last one emitted -- so the burst of light/effect/load-reference lines a single arrival
/// produces collapses to one <see cref="PlayerLocationEvent"/>. The token has no numeric place id,
/// so it is a soft hint the applier uses to tag or park drops, never to mint a place.
/// </summary>
public sealed partial class PlayerLocationParser : IInventoryEventParser
{
    // Trailing underscore-delimited token of the "…Manager [LoadingPlatform…_<token>]" name. The
    // greedy prefix backtracks to the final underscore, so multi-segment names yield their last part
    // (…_HangarMedium_Nyx -> "Nyx", …_Exterior_Manager_Levski -> "Levski").
    [GeneratedRegex(@"Manager \[LoadingPlatform[^\]]*_([A-Za-z0-9]+)\]", RegexOptions.Compiled)]
    private static partial Regex PlatformManagerToken();

    private string? _lastToken;

    public bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev)
    {
        ev = null;

        if (entry.Category != "CSCLoadingPlatformManager::OnLoadingPlatformStateChanged"
            || !entry.Message.Contains("changed to Open", StringComparison.Ordinal))
            return false;

        var m = PlatformManagerToken().Match(entry.Message);
        if (!m.Success)
            return false;

        var token = m.Groups[1].Value;
        if (token == _lastToken)
            return false; // same platform still cycling — this location is already reported.

        _lastToken = token;
        ev = new PlayerLocationEvent(entry.Timestamp, token);
        return true;
    }
}

/// <summary>
/// Stateful: names a placed storage container (a "Stor-All" SCU box) by pairing the
/// <c>OpenNestedInventory</c> request that carries its <c>Carryable_TBO_InventoryContainer_NSCU</c>
/// class with the numeric-ref line (<c>&lt;Query Inventory&gt;</c>, older builds
/// <c>&lt;RequestInventory&gt;</c>) that immediately follows and reports the box's own
/// <c>GEID:Container:0</c> ref. The open line only carries the class and the *location* the box sits
/// at, never the box's GEID; the follow-up query carries the GEID but not the class — so both are
/// needed to attach a size/name to the identity holdings key off.
///
/// The open line stashes state and reports no event (the existing <see cref="ContainerOpenedParser"/>
/// still emits its open event); the paired query emits a <see cref="ContainerIdentifiedEvent"/>. Only
/// classes matching <c>_NSCU</c> qualify, so ordinary nested opens are ignored. Lines are processed in
/// log order on a single thread; this parser must run before <see cref="ContainerOpenedParser"/> so it
/// sees the open line first.
/// </summary>
public sealed partial class NestedContainerParser : IInventoryEventParser
{
    // Numeric-ref line can trail the open by a few intervening lines (grid queries), but shares a
    // timestamp to within a fraction of a second; anything beyond this means the open was dangling.
    private static readonly TimeSpan PairWindow = TimeSpan.FromSeconds(2);

    // The game renamed this category from "RequestInventory" to "Query Inventory"; accept both.
    private static readonly string[] NumericRefCategories = ["RequestInventory", "Query Inventory"];

    [GeneratedRegex(@"(?<![0-9])(\d+)SCU", RegexOptions.Compiled)]
    private static partial Regex ScuSizeRegex();

    private long _pendingSize;
    private string? _pendingClass;
    private long _pendingParent;
    private DateTimeOffset _pendingAt;

    public bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev)
    {
        ev = null;

        if (entry.Category == "InventoryManagementRequest"
            && entry.Message.StartsWith("Queued Request", StringComparison.Ordinal)
            && LogFields.Get(entry.Message, "Type") == "OpenNestedInventory")
        {
            var cls = LogFields.Get(entry.Message, "Source");
            if (!string.IsNullOrEmpty(cls) && TryParseScu(cls, out var size))
            {
                _pendingClass = cls;
                _pendingSize = size;
                // The open line carries the place the box sits at (never the box's own GEID) as its
                // Source Inventory location ref; stash its place id so the box nests under it.
                _pendingParent =
                    InventoryRef.TryParse(LogFields.Get(entry.Message, "Source Inventory"), out var src)
                    && src.Kind == InventoryKind.Location
                        ? src.Id
                        : 0;
                _pendingAt = entry.Timestamp;
            }
            return false; // class stashed; the GEID (and the event) comes on the paired line.
        }

        if (Array.IndexOf(NumericRefCategories, entry.Category) < 0)
            return false;

        if (_pendingClass is null || entry.Timestamp - _pendingAt > PairWindow)
            return false;

        if (!InventoryRef.TryParse(LogFields.Get(entry.Message, "Inventory"), out var inv)
            || inv.Kind != InventoryKind.Container)
            return false;

        var pendingClass = _pendingClass;
        var pendingSize = _pendingSize;
        var pendingParent = _pendingParent;
        _pendingClass = null;
        _pendingParent = 0;

        // A container ref keys off its owning GEID (the 1st field); the Id (3rd field) is 0 here.
        ev = new ContainerIdentifiedEvent(entry.Timestamp, inv.Owner, pendingClass, (int)pendingSize, pendingParent);
        return true;
    }

    private static bool TryParseScu(string className, out long size)
    {
        var m = ScuSizeRegex().Match(className);
        return FieldHelpers.TryLong(m.Success ? m.Groups[1].Value : null, out size) && size > 0;
    }
}

