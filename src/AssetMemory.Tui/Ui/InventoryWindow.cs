using AssetMemory.Data;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace AssetMemory.Tui.Ui;

/// <summary>
/// The main TUI screen: a live-refreshing, filterable, sortable, paged inventory table — the console
/// analogue of the Blazor <c>Home.razor</c>. Reads go through <see cref="AssetMemoryStore"/> (same SQL
/// query surface as the web UI); writes go through <see cref="IActions"/>.
/// </summary>
public sealed class InventoryWindow : Window
{
    private static readonly int[] PageSizes = [10, 25, 50, 100];

    private readonly AssetMemoryStore _store;
    private readonly IActions _actions;

    // Query state (mirrors Home.razor)
    private string _searchTerm = "";
    private string? _selectedSystem;
    private long? _selectedPlaceId;
    private long? _selectedContainerId;
    private string _sortColumn = "item";
    private bool _sortAsc = true;
    private int _page = 1;
    private int _pageSize = 25;

    private HoldingDetailsPage _pageResult = new([], 0, 0, 0);
    private List<string> _systems = [];
    private List<LocationRow> _places = [];
    private List<LocationRow> _containers = [];

    // Controls
    private readonly TextField _searchField;
    private readonly Button _systemBtn;
    private readonly Button _locBtn;
    private readonly Button _containerBtn;
    private readonly Button _pageSizeBtn;
    private readonly Button _itemSortBtn;
    private readonly Button _locSortBtn;
    private readonly Button _qtySortBtn;
    private readonly Button _seenSortBtn;
    private readonly TableView _table;
    private readonly Label _statusLabel;
    private readonly Label _pageLabel;
    private readonly Button _prevBtn;
    private readonly Button _nextBtn;
    private readonly Label _watchingLabel;
    private readonly Label _breadcrumbLabel;

    public InventoryWindow(AssetMemoryStore store, IActions actions)
    {
        _store = store;
        _actions = actions;

        Title = "AssetMemory — Star Citizen inventory  (Ctrl+Q to quit)";

        // --- Row 0: search + filters ---
        var searchLabel = new Label { X = 1, Y = 0, Text = "Search:" };
        _searchField = new TextField { X = 9, Y = 0, Width = 28, Text = "" };
        _systemBtn = new Button { X = Pos.Right(_searchField) + 2, Y = 0, Text = "System: All" };
        _locBtn = new Button { X = Pos.Right(_systemBtn) + 1, Y = 0, Text = "Place: All" };
        // Only shown once the selected place has stocked containers to drill into.
        _containerBtn = new Button { X = Pos.Right(_locBtn) + 1, Y = 0, Text = "Container: (local)", Visible = false };
        _pageSizeBtn = new Button { X = Pos.Right(_containerBtn) + 1, Y = 0, Text = "Per page: 25" };
        var refreshBtn = new Button { X = Pos.Right(_pageSizeBtn) + 1, Y = 0, Text = "Refresh" };

        // --- Row 1: sort ---
        var sortLabel = new Label { X = 1, Y = 1, Text = "Sort:" };
        _itemSortBtn = new Button { X = 8, Y = 1, Text = "Item" };
        _locSortBtn = new Button { X = Pos.Right(_itemSortBtn) + 1, Y = 1, Text = "Location" };
        _qtySortBtn = new Button { X = Pos.Right(_locSortBtn) + 1, Y = 1, Text = "Qty" };
        _seenSortBtn = new Button { X = Pos.Right(_qtySortBtn) + 1, Y = 1, Text = "Last seen" };

        // --- Row 2: breadcrumb (mirrors Home.razor's "Place › Container" line) ---
        _breadcrumbLabel = new Label { X = 1, Y = 2, Width = Dim.Fill(1), Text = "All locations" };

        // --- Table ---
        _table = new TableView
        {
            X = 0,
            Y = 3,
            Width = Dim.Fill(),
            Height = Dim.Fill(4),
            FullRowSelect = true,
        };

        // --- Footer ---
        _statusLabel = new Label { X = 1, Y = Pos.Bottom(_table), Width = Dim.Fill(1), Text = "" };
        _prevBtn = new Button { X = 1, Y = Pos.Bottom(_table) + 1, Text = "< Prev" };
        _pageLabel = new Label { X = Pos.Right(_prevBtn) + 1, Y = Pos.Bottom(_table) + 1, Width = 16, Text = "Page 1/1" };
        _nextBtn = new Button { X = Pos.Right(_pageLabel) + 1, Y = Pos.Bottom(_table) + 1, Text = "Next >" };
        var syncBtn = new Button { X = Pos.Right(_nextBtn) + 3, Y = Pos.Bottom(_table) + 1, Text = "Sync backups" };
        var freshBtn = new Button { X = Pos.Right(syncBtn) + 1, Y = Pos.Bottom(_table) + 1, Text = "Start fresh" };
        var pathBtn = new Button { X = Pos.Right(freshBtn) + 1, Y = Pos.Bottom(_table) + 1, Text = "Change folder" };
        var inceptionBtn = new Button { X = Pos.Right(pathBtn) + 1, Y = Pos.Bottom(_table) + 1, Text = "Sync from…" };
        _watchingLabel = new Label { X = 1, Y = Pos.Bottom(_table) + 2, Width = Dim.Fill(1), Text = "" };

        // --- Events ---
        _searchField.ValueChanged += (_, _) =>
        {
            _searchTerm = _searchField.Text ?? "";
            _page = 1;
            Reload();
        };

        _systemBtn.Accepting += (_, _) => PickSystem();
        _locBtn.Accepting += (_, _) => PickPlace();
        _containerBtn.Accepting += (_, _) => PickContainer();
        _pageSizeBtn.Accepting += (_, _) =>
        {
            var idx = Array.IndexOf(PageSizes, _pageSize);
            _pageSize = PageSizes[(idx + 1) % PageSizes.Length];
            _pageSizeBtn.Text = $"Per page: {_pageSize}";
            _page = 1;
            Reload();
        };
        refreshBtn.Accepting += (_, _) => Reload();

        _itemSortBtn.Accepting += (_, _) => SetSort("item");
        _locSortBtn.Accepting += (_, _) => SetSort("location");
        _qtySortBtn.Accepting += (_, _) => SetSort("qty");
        _seenSortBtn.Accepting += (_, _) => SetSort("seen");

        _prevBtn.Accepting += (_, _) => { _page--; Reload(); };
        _nextBtn.Accepting += (_, _) => { _page++; Reload(); };

        syncBtn.Accepting += (_, _) =>
        {
            var result = _actions.Sync();
            _page = 1;
            Reload();
            Modals.Info("Sync complete", result.Message);
        };

        freshBtn.Accepting += (_, _) =>
        {
            if (Modals.Confirm("Start fresh", "Erase all tracked data and track forward only?", "Erase"))
            {
                _actions.Clear();
                _page = 1;
                Reload();
            }
        };

        pathBtn.Accepting += (_, _) =>
        {
            if (SetupDialog.Run(_actions))
            {
                _page = 1;
                Reload();
            }
        };

        inceptionBtn.Accepting += (_, _) => PickInception();

        Add(searchLabel, _searchField, _systemBtn, _locBtn, _containerBtn, _pageSizeBtn, refreshBtn,
            sortLabel, _itemSortBtn, _locSortBtn, _qtySortBtn, _seenSortBtn,
            _breadcrumbLabel,
            _table,
            _statusLabel, _prevBtn, _pageLabel, _nextBtn, syncBtn, freshBtn, pathBtn, inceptionBtn, _watchingLabel);

        UpdateSortButtons();
        Reload();

        // Live auto-refresh, matching the web UI's 3-second poll.
        Application.AddTimeout(TimeSpan.FromSeconds(3), () =>
        {
            Reload();
            return true;
        });
    }

    private void SetSort(string column)
    {
        if (_sortColumn == column)
            _sortAsc = !_sortAsc;
        else
        {
            _sortColumn = column;
            _sortAsc = true;
        }
        _page = 1;
        UpdateSortButtons();
        Reload();
    }

    private void UpdateSortButtons()
    {
        string Arrow(string col) => _sortColumn == col ? (_sortAsc ? " ^" : " v") : "";
        _itemSortBtn.Text = "Item" + Arrow("item");
        _locSortBtn.Text = "Location" + Arrow("location");
        _qtySortBtn.Text = "Qty" + Arrow("qty");
        _seenSortBtn.Text = "Last seen" + Arrow("seen");
    }

    private void PickSystem()
    {
        _systems = _store.GetSystemsWithHoldings().ToList();
        var items = new List<string> { "All systems" };
        items.AddRange(_systems);

        var current = _selectedSystem is null ? 0 : _systems.IndexOf(_selectedSystem) + 1;

        var choice = Modals.ChooseFromList("Filter by system", items, current);
        if (choice < 0)
            return;

        _selectedSystem = choice == 0 ? null : _systems[choice - 1];
        _selectedPlaceId = null;      // a new system invalidates any place/container selection
        _selectedContainerId = null;
        _page = 1;
        Reload();
    }

    private void PickPlace()
    {
        _places = _store.GetPlacesWithHoldings(_selectedSystem).ToList();
        var items = new List<string> { "All locations" };
        items.AddRange(_places.Select(l => l.Label ?? $"Location {l.Id}"));

        var current = _selectedPlaceId is null
            ? 0
            : _places.FindIndex(l => l.Id == _selectedPlaceId) + 1;

        var choice = Modals.ChooseFromList("Filter by place", items, current);
        if (choice < 0)
            return;

        _selectedPlaceId = choice == 0 ? null : _places[choice - 1].Id;
        _selectedContainerId = null;  // a new place invalidates any container selection
        _page = 1;
        Reload();
    }

    private void PickContainer()
    {
        if (_selectedPlaceId is not { } placeId)
            return;

        _containers = _store.GetContainersForPlace(placeId).ToList();
        if (_containers.Count == 0)
            return;

        var items = new List<string> { "(local storage)" };
        items.AddRange(_containers.Select(c => c.Label ?? $"Container {c.Id}"));

        var current = _selectedContainerId is null
            ? 0
            : _containers.FindIndex(c => c.Id == _selectedContainerId) + 1;

        var choice = Modals.ChooseFromList("Filter by container", items, current);
        if (choice < 0)
            return;

        _selectedContainerId = choice == 0 ? null : _containers[choice - 1].Id;
        _page = 1;
        Reload();
    }

    // Prompts for a sync-inception date (blank clears it), then rebuilds the DB under the new bound.
    private void PickInception()
    {
        var current = _actions.InceptionUtc is { } d ? d.ToString("yyyy-MM-dd") : "";
        var input = Modals.Prompt(
            "Sync inception",
            "Track from date (YYYY-MM-DD). Leave blank to ingest all history.",
            current);
        if (input is null)
            return; // cancelled

        input = input.Trim();
        DateTimeOffset? date;
        if (input.Length == 0)
            date = null;
        else if (DateOnly.TryParse(input, out var parsed))
            date = new DateTimeOffset(parsed.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); // 00:00 UTC
        else
        {
            Modals.Info("Invalid date", "Enter a date as YYYY-MM-DD, or leave it blank for all history.");
            return;
        }

        _actions.SetInception(date);
        _page = 1;
        Reload();
        Modals.Info("Rebuilt", date is { } dd
            ? $"Now tracking from {dd:yyyy-MM-dd} (UTC)."
            : "Now ingesting all history.");
    }

    private void Reload()
    {
        var term = string.IsNullOrWhiteSpace(_searchTerm) ? null : _searchTerm.Trim();

        // A system rolls up every place (and its boxes) tagged with it; a place further rolls up its
        // containers' contents; a chosen container narrows to just that box.
        _pageResult = _store.GetHoldingDetailsPage(_selectedPlaceId, _selectedContainerId, term, _sortColumn, _sortAsc, _page, _pageSize, _selectedSystem);
        _systems = _store.GetSystemsWithHoldings().ToList();
        _places = _store.GetPlacesWithHoldings(_selectedSystem).ToList();
        _containers = _selectedPlaceId is { } placeId
            ? _store.GetContainersForPlace(placeId).ToList()
            : [];

        var totalPages = Math.Max(1, (int)Math.Ceiling((double)_pageResult.TotalCount / _pageSize));
        _page = Math.Clamp(_page, 1, totalPages);

        _table.Table = BuildSource(_pageResult.Rows);
        _table.SetNeedsDraw();

        _statusLabel.Text =
            $"{_pageResult.TotalCount} rows | {_pageResult.DistinctLocations} locations | {_pageResult.TotalUnits} units";
        _pageLabel.Text = $"Page {_page}/{totalPages}";
        _prevBtn.Enabled = _page > 1;
        _nextBtn.Enabled = _page < totalPages;

        var systemName = _selectedSystem ?? "All";
        _systemBtn.Text = $"System: {systemName}";

        var placeName = _selectedPlaceId is null
            ? "All"
            : _places.FirstOrDefault(l => l.Id == _selectedPlaceId)?.Label ?? $"#{_selectedPlaceId}";
        _locBtn.Text = $"Place: {placeName}";

        // The container button only appears once the selected place actually has stocked boxes.
        _containerBtn.Visible = _containers.Count > 0;
        var containerName = _selectedContainerId is null
            ? "(local)"
            : _containers.FirstOrDefault(c => c.Id == _selectedContainerId)?.Label ?? $"#{_selectedContainerId}";
        _containerBtn.Text = $"Container: {containerName}";

        _breadcrumbLabel.Text = _selectedSystem is null
            ? "All locations"
            : _selectedPlaceId is null
                ? systemName
                : _selectedContainerId is null
                    ? $"{systemName} › {placeName}"
                    : $"{systemName} › {placeName} › {containerName}";

        var mode = _actions.IsViewer ? "viewer" : "standalone";
        var syncing = _actions.IsInitialSyncing ? "  (initial sync…)" : "";
        var from = _actions.InceptionUtc is { } inc ? $"  |  from {inc:yyyy-MM-dd}" : "";
        _watchingLabel.Text = $"[{mode}] Watching: {_actions.GameLogPath ?? "(not set)"}{syncing}{from}  |  item data: api.star-citizen.wiki";
    }

    private static EnumerableTableSource<HoldingDetail> BuildSource(IReadOnlyList<HoldingDetail> rows) =>
        new(rows, new Dictionary<string, Func<HoldingDetail, object>>
        {
            { "Item", h => h.ItemDisplayName ?? h.ItemClassName },
            { "Location", h => h.LocationParentLabel ?? h.LocationLabel ?? $"Location {h.LocationId}" },
            { "Local Storage", h => h.LocationParentLabel is not null ? h.LocationLabel ?? "" : "" },
            { "Qty", h => h.Quantity },
            { "Last seen", h => FormatTimeAgo(h.LastSeenUtc) },
        });

    private static string FormatTimeAgo(DateTimeOffset utc)
    {
        var diff = DateTimeOffset.UtcNow - utc;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays}d ago";
        return utc.ToString("yyyy-MM-dd");
    }
}
