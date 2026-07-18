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
    private long? _selectedLocationId;
    private string _sortColumn = "item";
    private bool _sortAsc = true;
    private int _page = 1;
    private int _pageSize = 25;

    private HoldingDetailsPage _pageResult = new([], 0, 0, 0);
    private List<LocationRow> _locations = [];

    // Controls
    private readonly TextField _searchField;
    private readonly Button _locBtn;
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

    public InventoryWindow(AssetMemoryStore store, IActions actions)
    {
        _store = store;
        _actions = actions;

        Title = "AssetMemory — Star Citizen inventory  (Ctrl+Q to quit)";

        // --- Row 0: search + filters ---
        var searchLabel = new Label { X = 1, Y = 0, Text = "Search:" };
        _searchField = new TextField { X = 9, Y = 0, Width = 28, Text = "" };
        _locBtn = new Button { X = Pos.Right(_searchField) + 2, Y = 0, Text = "Location: All" };
        _pageSizeBtn = new Button { X = Pos.Right(_locBtn) + 1, Y = 0, Text = "Per page: 25" };
        var refreshBtn = new Button { X = Pos.Right(_pageSizeBtn) + 1, Y = 0, Text = "Refresh" };

        // --- Row 1: sort ---
        var sortLabel = new Label { X = 1, Y = 1, Text = "Sort:" };
        _itemSortBtn = new Button { X = 8, Y = 1, Text = "Item" };
        _locSortBtn = new Button { X = Pos.Right(_itemSortBtn) + 1, Y = 1, Text = "Location" };
        _qtySortBtn = new Button { X = Pos.Right(_locSortBtn) + 1, Y = 1, Text = "Qty" };
        _seenSortBtn = new Button { X = Pos.Right(_qtySortBtn) + 1, Y = 1, Text = "Last seen" };

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
        _watchingLabel = new Label { X = 1, Y = Pos.Bottom(_table) + 2, Width = Dim.Fill(1), Text = "" };

        // --- Events ---
        _searchField.ValueChanged += (_, _) =>
        {
            _searchTerm = _searchField.Text ?? "";
            _page = 1;
            Reload();
        };

        _locBtn.Accepting += (_, _) => PickLocation();
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

        Add(searchLabel, _searchField, _locBtn, _pageSizeBtn, refreshBtn,
            sortLabel, _itemSortBtn, _locSortBtn, _qtySortBtn, _seenSortBtn,
            _table,
            _statusLabel, _prevBtn, _pageLabel, _nextBtn, syncBtn, freshBtn, pathBtn, _watchingLabel);

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

    private void PickLocation()
    {
        _locations = _store.GetLocationsWithHoldings().ToList();
        var items = new List<string> { "All locations" };
        items.AddRange(_locations.Select(l => l.Label ?? $"Location {l.Id}"));

        var current = _selectedLocationId is null
            ? 0
            : _locations.FindIndex(l => l.Id == _selectedLocationId) + 1;

        var choice = Modals.ChooseFromList("Filter by location", items, current);
        if (choice < 0)
            return;

        _selectedLocationId = choice == 0 ? null : _locations[choice - 1].Id;
        _page = 1;
        Reload();
    }

    private void Reload()
    {
        var term = string.IsNullOrWhiteSpace(_searchTerm) ? null : _searchTerm.Trim();
        _pageResult = _store.GetHoldingDetailsPage(_selectedLocationId, term, _sortColumn, _sortAsc, _page, _pageSize);
        _locations = _store.GetLocationsWithHoldings().ToList();

        var totalPages = Math.Max(1, (int)Math.Ceiling((double)_pageResult.TotalCount / _pageSize));
        _page = Math.Clamp(_page, 1, totalPages);

        _table.Table = BuildSource(_pageResult.Rows);
        _table.SetNeedsDraw();

        _statusLabel.Text =
            $"{_pageResult.TotalCount} rows | {_pageResult.DistinctLocations} locations | {_pageResult.TotalUnits} units";
        _pageLabel.Text = $"Page {_page}/{totalPages}";
        _prevBtn.Enabled = _page > 1;
        _nextBtn.Enabled = _page < totalPages;

        var locName = _selectedLocationId is null
            ? "All"
            : _locations.FirstOrDefault(l => l.Id == _selectedLocationId)?.Label ?? $"#{_selectedLocationId}";
        _locBtn.Text = $"Location: {locName}";

        var mode = _actions.IsViewer ? "viewer" : "standalone";
        var syncing = _actions.IsInitialSyncing ? "  (initial sync…)" : "";
        _watchingLabel.Text = $"[{mode}] Watching: {_actions.GameLogPath ?? "(not set)"}{syncing}";
    }

    private static EnumerableTableSource<HoldingDetail> BuildSource(IReadOnlyList<HoldingDetail> rows) =>
        new(rows, new Dictionary<string, Func<HoldingDetail, object>>
        {
            { "Item", h => h.ItemDisplayName ?? h.ItemClassName },
            { "Location", h => h.LocationLabel ?? $"Location {h.LocationId}" },
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
