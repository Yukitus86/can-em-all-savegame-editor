using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CanEmAllEditor;

public enum DialogButtons { Ok, YesNo, YesNoCancel }
public enum DialogResult { None, Ok, Yes, No, Cancel }

/// <summary>Avalonia has no message box: a small dark dialog with the editor's look.</summary>
static class Dialog
{
    public static async Task<DialogResult> Show(Window owner, string text, DialogButtons buttons = DialogButtons.Ok, bool warn = false)
    {
        var win = new Window
        {
            Title = "Can 'Em All",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res(owner, "Panel"),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };

        void Add(string label, DialogResult result, bool accent)
        {
            var b = new Button { Content = label, MinWidth = 90 };
            if (accent) b.Classes.Add("accent");
            b.Click += (_, _) => win.Close(result);
            row.Children.Add(b);
        }

        var t = CanEmAllEditor.Core.Loc.Instance;
        switch (buttons)
        {
            case DialogButtons.Ok: Add("OK", DialogResult.Ok, true); break;
            case DialogButtons.YesNo:
                Add(t["Yes"], DialogResult.Yes, true);
                Add(t["No"], DialogResult.No, false);
                break;
            default:
                Add(t["Yes"], DialogResult.Yes, true);
                Add(t["No"], DialogResult.No, false);
                Add(t["Cancel"], DialogResult.Cancel, false);
                break;
        }

        win.Content = new Border
        {
            Padding = new Thickness(22, 20),
            BorderBrush = Res(owner, warn ? "Warn" : "Line"),
            BorderThickness = new Thickness(0, 3, 0, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 20 },
                    row,
                },
            },
        };
        var r = await win.ShowDialog<DialogResult?>(owner);
        return r ?? (buttons == DialogButtons.YesNoCancel ? DialogResult.Cancel : buttons == DialogButtons.YesNo ? DialogResult.No : DialogResult.Ok);
    }

    static IBrush Res(Window w, string key) => w.TryFindResource(key, out var v) && v is IBrush b ? b : Brushes.Gray;
}
