using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace CanEmAllEditor;

public partial class App : Application
{
    /// <summary>"--root &lt;dir&gt;" points the editor at a copy of the save folder (testing).</summary>
    public static string? RootOverride { get; private set; }

    /// <summary>"--snapshot &lt;dir&gt;" renders every tab to PNG files and exits (UI check without clicking).</summary>
    public static string? SnapshotDir { get; private set; }

    /// <summary>"--lang de|en" overrides the saved language for this run.</summary>
    public static string? LanguageOverride { get; private set; }

    /// <summary>"--selftest &lt;logfile&gt;" edits the loaded save, saves it, reads it back and logs the result.</summary>
    public static string? SelfTestLog { get; private set; }

    /// <summary>"--mask-user" hides the user name in displayed paths (screenshots).</summary>
    public static bool MaskUser { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            for (int i = 0; i < args.Length; i++)
            {
                string? next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "--root" when next is not null: RootOverride = next; i++; break;
                    case "--snapshot" when next is not null: SnapshotDir = next; i++; break;
                    case "--lang" when next is not null: LanguageOverride = next; i++; break;
                    case "--selftest" when next is not null: SelfTestLog = next; i++; break;
                    case "--mask-user": MaskUser = true; break;
                }
            }
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Path for display; with --mask-user the home folder becomes "~".</summary>
    public static string ShowPath(string? path)
    {
        if (path is null || !MaskUser) return path ?? "";
        // A test copy (--root) is shown at the usual Proton location, so screenshots look like a normal install.
        string? copyParent = RootOverride is null ? null : Path.GetDirectoryName(Path.GetFullPath(RootOverride));
        path = Path.GetFullPath(path);
        if (copyParent is not null && path.StartsWith(copyParent, StringComparison.OrdinalIgnoreCase))
            return "~/.local/share/Steam/steamapps/compatdata/5133900/pfx/drive_c/users/steamuser/AppData/LocalLow/LuisDev"
                   + path[copyParent.Length..].Replace('\\', '/');
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }
}
