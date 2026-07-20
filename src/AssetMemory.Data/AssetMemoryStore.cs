using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AssetMemory.Data;

/// <summary>
/// Façade over the AssetMemory SQLite schema. The caller owns the <see cref="SqliteConnection"/>;
/// the store applies the schema and exposes typed CRUD/query helpers. Timestamps are persisted
/// as ISO-8601 UTC strings so they sort lexicographically and survive locale changes.
/// </summary>
public sealed class AssetMemoryStore
{
    private const int CurrentSchemaVersion = 2;

    private readonly SqliteConnection _conn;

    public AssetMemoryStore(SqliteConnection conn)
        => _conn = conn ?? throw new ArgumentNullException(nameof(conn));

    public int SchemaVersion
    {
        get
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public void ApplyMigration()
    {
        // WAL lets the UI's read queries run without blocking on an in-progress sync, and
        // synchronous=NORMAL is safe under WAL (SQLite docs: only the most recent commit can
        // be lost on power failure, never corruption) while skipping most per-commit fsyncs.
        Exec("PRAGMA journal_mode = WAL;");
        Exec("PRAGMA synchronous = NORMAL;");
        Exec("PRAGMA foreign_keys = ON;");
        Exec("""
            CREATE TABLE IF NOT EXISTS locations (
                id            INTEGER PRIMARY KEY,
                label         TEXT,
                parent_id     INTEGER,
                last_seen_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS items (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                class_name    TEXT NOT NULL UNIQUE,
                display_name  TEXT
            );
            CREATE TABLE IF NOT EXISTS holdings (
                location_id    INTEGER NOT NULL REFERENCES locations(id) ON DELETE CASCADE,
                item_id        INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                quantity       INTEGER NOT NULL CHECK (quantity > 0),
                first_seen_utc TEXT NOT NULL,
                last_seen_utc  TEXT NOT NULL,
                PRIMARY KEY (location_id, item_id)
            );
            CREATE INDEX IF NOT EXISTS ix_holdings_item ON holdings(item_id);
            CREATE TABLE IF NOT EXISTS equipped (
                player        TEXT NOT NULL,
                port          TEXT NOT NULL,
                item_id       INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                entity_id     INTEGER NOT NULL,
                instance_name TEXT,
                status        TEXT,
                last_seen_utc TEXT NOT NULL,
                PRIMARY KEY (player, port)
            );
            CREATE TABLE IF NOT EXISTS events_audit (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                utc  TEXT NOT NULL,
                type TEXT NOT NULL,
                raw  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS external_item_names (
                class_name   TEXT PRIMARY KEY,
                display_name TEXT NOT NULL
            );
            """);

        // v1 -> v2: the place->container hierarchy adds locations.parent_id. Fresh DBs get it from
        // the CREATE above; older v1 DBs are upgraded in place here. Idempotent: guarded on presence.
        if (!ColumnExists("locations", "parent_id"))
            Exec("ALTER TABLE locations ADD COLUMN parent_id INTEGER;");

        Exec($"PRAGMA user_version = {CurrentSchemaVersion};");
    }

    /// <summary>True if <paramref name="table"/> already has a column named <paramref name="column"/>.</summary>
    private bool ColumnExists(string table, string column)
    {
        // table/column are internal literals, never user input — safe to interpolate (PRAGMA
        // table_info does not accept a bound parameter for its argument).
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // -------- batching --------

    /// <summary>
    /// Wraps subsequent writes in one SQLite transaction so they commit (and fsync) together
    /// instead of each autocommitting on its own -- the difference between one disk sync and
    /// thousands when applying a large batch of events.
    /// </summary>
    public void BeginTransaction() => Exec("BEGIN;");

    public void CommitTransaction() => Exec("COMMIT;");

    public void RollbackTransaction() => Exec("ROLLBACK;");

    // -------- locations --------

    public void UpsertLocation(long id, DateTimeOffset lastSeenUtc, string? label)
    {
        // Insert or update — but only overwrite an existing label if a non-null one is supplied.
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO locations (id, label, last_seen_utc)
            VALUES ($id, $label, $utc)
            ON CONFLICT(id) DO UPDATE SET
                last_seen_utc = excluded.last_seen_utc,
                label         = COALESCE(excluded.label, label);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$label", (object?)label ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$utc", FormatUtc(lastSeenUtc));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Upserts a container row (a box nested inside a place). Like <see cref="UpsertLocation"/> but
    /// also records the <paramref name="parentId"/> place it sits in. Both the label and the parent
    /// are written with COALESCE so a null label / non-positive parent never clobbers identity a
    /// previous line already established -- a later bare open of the same box is a no-op on both.
    /// </summary>
    public void UpsertContainer(long id, long parentId, DateTimeOffset lastSeenUtc, string? label)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO locations (id, label, parent_id, last_seen_utc)
            VALUES ($id, $label, $parent, $utc)
            ON CONFLICT(id) DO UPDATE SET
                last_seen_utc = excluded.last_seen_utc,
                label         = COALESCE(excluded.label, label),
                parent_id     = COALESCE(excluded.parent_id, parent_id);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$label", (object?)label ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$parent", parentId > 0 ? parentId : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$utc", FormatUtc(lastSeenUtc));
        cmd.ExecuteNonQuery();
    }

    public LocationRow? GetLocation(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, label, last_seen_utc FROM locations WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new LocationRow(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1), ParseUtc(r.GetString(2)))
            : null;
    }

    // -------- items --------

    /// <summary>
    /// Inserts or updates an item's display name. A row in <c>external_item_names</c> (durable —
    /// survives <see cref="ClearAll"/> and sync-inception rebuilds) always wins over the freshly
    /// computed <paramref name="displayName"/>: without this, "Start fresh" / changing the sync
    /// inception date wipes and rebuilds the <c>items</c> table from the log, and the rebuild only
    /// knows global.ini + the heuristic formatter, so a name backfilled from the external API earlier
    /// would silently revert until the next full app restart re-ran the backfill sweep.
    /// </summary>
    public long EnsureItem(string className, string? displayName)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO items (class_name, display_name)
            VALUES ($cn, COALESCE((SELECT display_name FROM external_item_names WHERE class_name = $cn), $dn))
            ON CONFLICT(class_name) DO UPDATE SET
                display_name = COALESCE(
                    (SELECT display_name FROM external_item_names WHERE class_name = $cn),
                    excluded.display_name,
                    display_name)
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$cn", className);
        cmd.Parameters.AddWithValue("$dn", (object?)displayName ?? DBNull.Value);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Durably caches an externally-resolved display name, independent of <c>items</c> so it isn't
    /// lost when that table gets wiped and rebuilt (see <see cref="EnsureItem"/>). Never cleared by
    /// <see cref="ClearAll"/> — it's item-catalog knowledge, not user inventory data, and re-deriving
    /// it means hitting the (rate-limited) external API again for no reason.
    /// </summary>
    public void UpsertExternalItemName(string className, string displayName)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentException.ThrowIfNullOrEmpty(displayName);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO external_item_names (class_name, display_name)
            VALUES ($cn, $dn)
            ON CONFLICT(class_name) DO UPDATE SET display_name = excluded.display_name;
            """;
        cmd.Parameters.AddWithValue("$cn", className);
        cmd.Parameters.AddWithValue("$dn", displayName);
        cmd.ExecuteNonQuery();
    }

    public ItemRow? GetItem(string className)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, class_name, display_name FROM items WHERE class_name = $cn;";
        cmd.Parameters.AddWithValue("$cn", className);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new ItemRow(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2))
            : null;
    }

    public IEnumerable<ItemRow> GetAllItems()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, class_name, display_name FROM items;";
        using var r = cmd.ExecuteReader();
        var list = new List<ItemRow>();
        while (r.Read())
            list.Add(new ItemRow(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    // -------- holdings --------

    public void AdjustHolding(long locationId, long itemId, int delta, DateTimeOffset atUtc)
    {
        if (delta == 0)
            return;

        var existing = GetHolding(locationId, itemId);
        var newQty = (existing?.Quantity ?? 0) + delta;

        if (newQty <= 0)
        {
            if (existing is not null)
            {
                using var del = _conn.CreateCommand();
                del.CommandText = "DELETE FROM holdings WHERE location_id = $l AND item_id = $i;";
                del.Parameters.AddWithValue("$l", locationId);
                del.Parameters.AddWithValue("$i", itemId);
                del.ExecuteNonQuery();
            }
            return;
        }

        var utc = FormatUtc(atUtc);
        if (existing is null)
        {
            using var ins = _conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO holdings (location_id, item_id, quantity, first_seen_utc, last_seen_utc)
                VALUES ($l, $i, $q, $u, $u);
                """;
            ins.Parameters.AddWithValue("$l", locationId);
            ins.Parameters.AddWithValue("$i", itemId);
            ins.Parameters.AddWithValue("$q", newQty);
            ins.Parameters.AddWithValue("$u", utc);
            ins.ExecuteNonQuery();
        }
        else
        {
            using var upd = _conn.CreateCommand();
            upd.CommandText = """
                UPDATE holdings SET quantity = $q, last_seen_utc = $u
                WHERE location_id = $l AND item_id = $i;
                """;
            upd.Parameters.AddWithValue("$l", locationId);
            upd.Parameters.AddWithValue("$i", itemId);
            upd.Parameters.AddWithValue("$q", newQty);
            upd.Parameters.AddWithValue("$u", utc);
            upd.ExecuteNonQuery();
        }
    }

    public HoldingRow? GetHolding(long locationId, long itemId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT location_id, item_id, quantity, first_seen_utc, last_seen_utc
            FROM holdings WHERE location_id = $l AND item_id = $i;
            """;
        cmd.Parameters.AddWithValue("$l", locationId);
        cmd.Parameters.AddWithValue("$i", itemId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadHolding(r) : null;
    }

    public IEnumerable<HoldingRow> GetHoldingsForLocation(long locationId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT location_id, item_id, quantity, first_seen_utc, last_seen_utc
            FROM holdings WHERE location_id = $l;
            """;
        cmd.Parameters.AddWithValue("$l", locationId);
        using var r = cmd.ExecuteReader();
        var list = new List<HoldingRow>();
        while (r.Read()) list.Add(ReadHolding(r));
        return list;
    }

    public IEnumerable<HoldingRow> FindLocationsHoldingItem(long itemId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT location_id, item_id, quantity, first_seen_utc, last_seen_utc
            FROM holdings WHERE item_id = $i AND quantity > 0;
            """;
        cmd.Parameters.AddWithValue("$i", itemId);
        using var r = cmd.ExecuteReader();
        var list = new List<HoldingRow>();
        while (r.Read()) list.Add(ReadHolding(r));
        return list;
    }

    // -------- equipped --------

    public void UpsertEquipped(
        string player, string port, long itemId, long entityId,
        string? instanceName, string? status, DateTimeOffset atUtc)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO equipped (player, port, item_id, entity_id, instance_name, status, last_seen_utc)
            VALUES ($p, $port, $i, $e, $n, $s, $u)
            ON CONFLICT(player, port) DO UPDATE SET
                item_id       = excluded.item_id,
                entity_id     = excluded.entity_id,
                instance_name = excluded.instance_name,
                status        = excluded.status,
                last_seen_utc = excluded.last_seen_utc;
            """;
        cmd.Parameters.AddWithValue("$p", player);
        cmd.Parameters.AddWithValue("$port", port);
        cmd.Parameters.AddWithValue("$i", itemId);
        cmd.Parameters.AddWithValue("$e", entityId);
        cmd.Parameters.AddWithValue("$n", (object?)instanceName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$u", FormatUtc(atUtc));
        cmd.ExecuteNonQuery();
    }

    public EquippedRow? GetEquipped(string player, string port)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT player, port, item_id, entity_id, instance_name, status, last_seen_utc
            FROM equipped WHERE player = $p AND port = $port;
            """;
        cmd.Parameters.AddWithValue("$p", player);
        cmd.Parameters.AddWithValue("$port", port);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadEquipped(r) : null;
    }

    public IEnumerable<EquippedRow> GetEquippedForPlayer(string player)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT player, port, item_id, entity_id, instance_name, status, last_seen_utc
            FROM equipped WHERE player = $p;
            """;
        cmd.Parameters.AddWithValue("$p", player);
        using var r = cmd.ExecuteReader();
        var list = new List<EquippedRow>();
        while (r.Read()) list.Add(ReadEquipped(r));
        return list;
    }

    // -------- query views --------

    public IEnumerable<HoldingDetail> GetAllHoldingDetails()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT h.location_id, l.label, p.label,
                   h.item_id, i.class_name, i.display_name,
                   h.quantity, h.first_seen_utc, h.last_seen_utc
            FROM holdings h
            JOIN items i ON i.id = h.item_id
            JOIN locations l ON l.id = h.location_id
            LEFT JOIN locations p ON p.id = l.parent_id
            ORDER BY l.label, i.display_name, i.class_name;
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<HoldingDetail>();
        while (r.Read())
            list.Add(new HoldingDetail(
                r.GetInt64(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetInt64(3),
                r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetInt32(6),
                ParseUtc(r.GetString(7)),
                ParseUtc(r.GetString(8))));
        return list;
    }

    /// <summary>
    /// Filtered, sorted, paged holding rows over labelled locations. Location scope rolls up:
    /// <paramref name="containerId"/> set -> just that container; else <paramref name="placeId"/>
    /// set -> that place's direct items PLUS every child container's contents; else both null ->
    /// everything (places and containers). So an item moved into a box (from a backpack, local
    /// storage, anywhere) still shows under its parent place, and narrowing to the box shows only
    /// its contents. Filtering, sorting, and paging all happen in SQL so the UI never has to load
    /// the whole holdings table to show one page of it.
    /// </summary>
    public HoldingDetailsPage GetHoldingDetailsPage(
        long? placeId, long? containerId, string? searchTerm, string sortColumn, bool sortAscending, int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        var sortExpr = SortExpression(sortColumn);
        var term = string.IsNullOrWhiteSpace(searchTerm) ? null : $"%{searchTerm}%";

        // Scope precedence: a chosen container pins to itself; otherwise a chosen place includes its
        // own rows and any container whose parent_id points back at it; otherwise no location filter.
        const string filterSql = """
            FROM holdings h
            JOIN items i ON i.id = h.item_id
            JOIN locations l ON l.id = h.location_id
            LEFT JOIN locations p ON p.id = l.parent_id
            WHERE l.label IS NOT NULL
              AND ($container IS NULL OR h.location_id = $container)
              AND ($container IS NOT NULL OR $place IS NULL
                   OR h.location_id = $place OR l.parent_id = $place)
              AND ($term IS NULL OR i.display_name LIKE $term OR i.class_name LIKE $term)
            """;

        using var countCmd = _conn.CreateCommand();
        countCmd.CommandText = $"""
            SELECT COUNT(*), COUNT(DISTINCT h.location_id), COALESCE(SUM(h.quantity), 0)
            {filterSql};
            """;
        countCmd.Parameters.AddWithValue("$place", (object?)placeId ?? DBNull.Value);
        countCmd.Parameters.AddWithValue("$container", (object?)containerId ?? DBNull.Value);
        countCmd.Parameters.AddWithValue("$term", (object?)term ?? DBNull.Value);
        int totalCount; int distinctLocations; long totalUnits;
        using (var cr = countCmd.ExecuteReader())
        {
            cr.Read();
            totalCount = cr.GetInt32(0);
            distinctLocations = cr.GetInt32(1);
            totalUnits = cr.GetInt64(2);
        }

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT h.location_id, l.label, p.label,
                   h.item_id, i.class_name, i.display_name,
                   h.quantity, h.first_seen_utc, h.last_seen_utc
            {filterSql}
            ORDER BY {sortExpr} {(sortAscending ? "ASC" : "DESC")}
            LIMIT $take OFFSET $skip;
            """;
        cmd.Parameters.AddWithValue("$place", (object?)placeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$container", (object?)containerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$term", (object?)term ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$take", pageSize);
        cmd.Parameters.AddWithValue("$skip", (page - 1) * pageSize);
        using var r = cmd.ExecuteReader();
        var rows = new List<HoldingDetail>();
        while (r.Read())
            rows.Add(new HoldingDetail(
                r.GetInt64(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetInt64(3),
                r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetInt32(6),
                ParseUtc(r.GetString(7)),
                ParseUtc(r.GetString(8))));

        return new HoldingDetailsPage(rows, totalCount, distinctLocations, totalUnits);
    }

    private static string SortExpression(string column) => column switch
    {
        "item" => "COALESCE(i.display_name, i.class_name) COLLATE NOCASE",
        "location" => "l.label COLLATE NOCASE",
        "qty" => "h.quantity",
        "seen" => "h.last_seen_utc",
        _ => throw new ArgumentException($"Unknown sort column '{column}'.", nameof(column)),
    };

    /// <summary>Distinct labelled locations that currently hold at least one item -- backs the location filter dropdown.</summary>
    public IEnumerable<LocationRow> GetLocationsWithHoldings()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT l.id, l.label, l.last_seen_utc
            FROM holdings h
            JOIN locations l ON l.id = h.location_id
            WHERE l.label IS NOT NULL
            ORDER BY l.label;
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<LocationRow>();
        while (r.Read())
            list.Add(new LocationRow(r.GetInt64(0), r.GetString(1), ParseUtc(r.GetString(2))));
        return list;
    }

    /// <summary>
    /// Labelled places (<c>parent_id IS NULL</c>) that either hold items directly or have a child
    /// container that does -- backs the top place dropdown. A place whose only holdings live inside
    /// a box still appears here so the user can drill into it.
    /// </summary>
    public IEnumerable<LocationRow> GetPlacesWithHoldings()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT l.id, l.label, l.last_seen_utc
            FROM locations l
            WHERE l.label IS NOT NULL
              AND l.parent_id IS NULL
              AND (EXISTS (SELECT 1 FROM holdings h WHERE h.location_id = l.id)
                OR EXISTS (SELECT 1 FROM locations c
                           JOIN holdings hc ON hc.location_id = c.id
                           WHERE c.parent_id = l.id))
            ORDER BY l.label;
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<LocationRow>();
        while (r.Read())
            list.Add(new LocationRow(r.GetInt64(0), r.GetString(1), ParseUtc(r.GetString(2))));
        return list;
    }

    /// <summary>
    /// Labelled containers (<c>parent_id = placeId</c>) sitting at <paramref name="placeId"/> that
    /// currently hold at least one item -- backs the second (container) dropdown. Empty when the
    /// place has no stocked boxes, in which case the UI need not render the dropdown at all.
    /// </summary>
    public IEnumerable<LocationRow> GetContainersForPlace(long placeId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT l.id, l.label, l.last_seen_utc
            FROM locations l
            JOIN holdings h ON h.location_id = l.id
            WHERE l.label IS NOT NULL
              AND l.parent_id = $place
            ORDER BY l.label;
            """;
        cmd.Parameters.AddWithValue("$place", placeId);
        using var r = cmd.ExecuteReader();
        var list = new List<LocationRow>();
        while (r.Read())
            list.Add(new LocationRow(r.GetInt64(0), r.GetString(1), ParseUtc(r.GetString(2))));
        return list;
    }

    public IEnumerable<ItemRow> SearchItems(string term)
    {
        ArgumentException.ThrowIfNullOrEmpty(term);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT i.id, i.class_name, i.display_name
            FROM items i
            JOIN holdings h ON h.item_id = i.id
            WHERE i.class_name LIKE $term OR i.display_name LIKE $term
            ORDER BY i.display_name, i.class_name;
            """;
        cmd.Parameters.AddWithValue("$term", $"%{term}%");
        using var r = cmd.ExecuteReader();
        var list = new List<ItemRow>();
        while (r.Read())
            list.Add(new ItemRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    public IEnumerable<ItemLocationDetail> GetItemLocationDetails(long itemId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT h.location_id, l.label,
                   h.quantity, h.first_seen_utc, h.last_seen_utc
            FROM holdings h
            JOIN locations l ON l.id = h.location_id
            WHERE h.item_id = $i AND h.quantity > 0
            ORDER BY l.label;
            """;
        cmd.Parameters.AddWithValue("$i", itemId);
        using var r = cmd.ExecuteReader();
        var list = new List<ItemLocationDetail>();
        while (r.Read())
            list.Add(new ItemLocationDetail(
                r.GetInt64(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.GetInt32(2),
                ParseUtc(r.GetString(3)),
                ParseUtc(r.GetString(4))));
        return list;
    }

    public IEnumerable<EquippedDetail> GetEquippedDetails(string player)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT e.player, e.port,
                   e.item_id, i.class_name, i.display_name,
                   e.entity_id, e.instance_name, e.status, e.last_seen_utc
            FROM equipped e
            JOIN items i ON i.id = e.item_id
            WHERE e.player = $p
            ORDER BY e.port;
            """;
        cmd.Parameters.AddWithValue("$p", player);
        using var r = cmd.ExecuteReader();
        var list = new List<EquippedDetail>();
        while (r.Read())
            list.Add(new EquippedDetail(
                r.GetString(0),
                r.GetString(1),
                r.GetInt64(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetInt64(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                ParseUtc(r.GetString(8))));
        return list;
    }

    // -------- audit --------

    public void RecordAudit(DateTimeOffset utc, string type, string raw)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO events_audit (utc, type, raw) VALUES ($u, $t, $r);";
        cmd.Parameters.AddWithValue("$u", FormatUtc(utc));
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$r", raw);
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<AuditRow> ReadAudit()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, utc, type, raw FROM events_audit ORDER BY id ASC;";
        using var r = cmd.ExecuteReader();
        var list = new List<AuditRow>();
        while (r.Read())
            list.Add(new AuditRow(r.GetInt64(0), ParseUtc(r.GetString(1)), r.GetString(2), r.GetString(3)));
        return list;
    }

    // -------- clear --------

    // Deliberately does NOT touch external_item_names -- see EnsureItem/UpsertExternalItemName.
    public void ClearAll()
    {
        Exec("DELETE FROM events_audit;");
        Exec("DELETE FROM holdings;");
        Exec("DELETE FROM equipped;");
        Exec("DELETE FROM items;");
        Exec("DELETE FROM locations;");
    }

    // -------- helpers --------

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string FormatUtc(DateTimeOffset dto)
        => dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string text)
        => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static HoldingRow ReadHolding(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetInt32(2),
        ParseUtc(r.GetString(3)), ParseUtc(r.GetString(4)));

    private static EquippedRow ReadEquipped(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        ParseUtc(r.GetString(6)));
}
