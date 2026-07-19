using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace AssetMemory.Tui.Ui;

/// <summary>
/// Small modal helpers built from verified primitives (Dialog + Button + ListView) rather than the
/// MessageBox helpers, to keep a stable API surface across Terminal.Gui point releases.
/// </summary>
internal static class Modals
{
    /// <summary>Yes/No confirmation. Returns true if the affirmative button was chosen.</summary>
    public static bool Confirm(string title, string message, string okText)
    {
        var result = false;
        var dialog = new Dialog { Title = title, Width = Dim.Percent(60), Height = 9 };

        var label = new Label { X = 1, Y = 1, Width = Dim.Fill(1), Text = message };
        var cancel = new Button { X = Pos.Center() - 12, Y = 5, Text = "Cancel" };
        var ok = new Button { X = Pos.Center() + 2, Y = 5, Text = okText, IsDefault = true };

        cancel.Accepting += (_, _) => Application.RequestStop();
        ok.Accepting += (_, _) => { result = true; Application.RequestStop(); };

        dialog.Add(label, cancel, ok);
        Application.Run(dialog);
        dialog.Dispose();
        return result;
    }

    /// <summary>Informational popup with a single OK button.</summary>
    public static void Info(string title, string message)
    {
        var dialog = new Dialog { Title = title, Width = Dim.Percent(60), Height = 9 };
        var label = new Label { X = 1, Y = 1, Width = Dim.Fill(1), Text = message };
        var ok = new Button { X = Pos.Center(), Y = 5, Text = "OK", IsDefault = true };
        ok.Accepting += (_, _) => Application.RequestStop();
        dialog.Add(label, ok);
        Application.Run(dialog);
        dialog.Dispose();
    }

    /// <summary>Text prompt with an OK/Cancel pair. Returns the entered text on OK, or null if cancelled.</summary>
    public static string? Prompt(string title, string message, string initial = "")
    {
        string? result = null;
        var dialog = new Dialog { Title = title, Width = Dim.Percent(60), Height = 10 };

        var label = new Label { X = 1, Y = 1, Width = Dim.Fill(1), Text = message };
        var field = new TextField { X = 1, Y = 3, Width = Dim.Fill(2), Text = initial };
        var ok = new Button { X = Pos.Center() - 12, Y = 6, Text = "OK", IsDefault = true };
        var cancel = new Button { X = Pos.Center() + 2, Y = 6, Text = "Cancel" };

        ok.Accepting += (_, _) => { result = field.Text ?? ""; Application.RequestStop(); };
        cancel.Accepting += (_, _) => Application.RequestStop();

        dialog.Add(label, field, ok, cancel);
        Application.Run(dialog);
        dialog.Dispose();
        return result;
    }

    /// <summary>Single-choice picker over a list. Returns the selected index, or -1 if cancelled.</summary>
    public static int ChooseFromList(string title, IReadOnlyList<string> items, int current)
    {
        var result = -1;
        var dialog = new Dialog { Title = title, Width = Dim.Percent(60), Height = Dim.Percent(70) };

        var list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };
        list.SetSource(new ObservableCollection<string>(items.ToList()));
        if (current >= 0 && current < items.Count)
            list.SelectedItem = current;

        var ok = new Button { X = Pos.Center() - 12, Y = Pos.AnchorEnd(1), Text = "Select", IsDefault = true };
        var cancel = new Button { X = Pos.Center() + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        ok.Accepting += (_, _) => { result = list.SelectedItem ?? -1; Application.RequestStop(); };
        cancel.Accepting += (_, _) => Application.RequestStop();

        dialog.Add(list, ok, cancel);
        Application.Run(dialog);
        dialog.Dispose();
        return result;
    }
}
