using AssetMemory.Core.Detection;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace AssetMemory.Tui.Ui;

/// <summary>First-run / change-folder dialog. Mirrors the Blazor setup card in <c>Home.razor</c>.</summary>
internal static class SetupDialog
{
    /// <summary>Returns true if a valid Game.log path was configured.</summary>
    public static bool Run(IActions actions)
    {
        var configured = false;
        var startFresh = false;

        var dialog = new Dialog { Title = "AssetMemory — Setup", Width = Dim.Percent(80), Height = 14 };

        var intro = new Label
        {
            X = 1, Y = 0, Width = Dim.Fill(1),
            Text = "Point AssetMemory at your Star Citizen install folder to start tracking.",
        };
        var pathLabel = new Label { X = 1, Y = 2, Text = "Folder:" };
        var pathField = new TextField { X = 9, Y = 2, Width = Dim.Fill(2), Text = "" };
        var freshBtn = new Button { X = 1, Y = 4, Text = "Start fresh: OFF" };
        var error = new Label { X = 1, Y = 6, Width = Dim.Fill(1), Text = "" };

        var auto = new Button { X = 1, Y = 9, Text = "Auto-detect" };
        var set = new Button { X = 18, Y = 9, Text = "Set", IsDefault = true };
        var quit = new Button { X = 26, Y = 9, Text = "Quit" };

        void Apply(string folder)
        {
            var res = actions.SetPath(folder, startFresh);
            if (res.Ok)
            {
                configured = true;
                Application.RequestStop();
            }
            else
            {
                error.Text = res.Error ?? "Could not set path.";
            }
        }

        freshBtn.Accepting += (_, _) =>
        {
            startFresh = !startFresh;
            freshBtn.Text = startFresh ? "Start fresh: ON" : "Start fresh: OFF";
        };

        auto.Accepting += (_, _) =>
        {
            var found = GamePathFinder.FindGameLogPaths();
            if (found.Count > 0)
                Apply(Path.GetDirectoryName(found[0]) ?? found[0]);
            else
                error.Text = "Could not find Game.log. Enter the folder manually.";
        };

        set.Accepting += (_, _) =>
        {
            var folder = (pathField.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(folder))
            {
                error.Text = "Please enter a path.";
                return;
            }
            Apply(folder);
        };

        quit.Accepting += (_, _) => Application.RequestStop();

        dialog.Add(intro, pathLabel, pathField, freshBtn, error, auto, set, quit);
        Application.Run(dialog);
        dialog.Dispose();
        return configured;
    }
}
