using System.IO;
using System.Windows;
using CanEmAllEditor.Core;

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

    /// <summary>"--mask-user" hides the Windows user name in displayed paths (screenshots).</summary>
    public static bool MaskUser { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        for (int i = 0; i < e.Args.Length; i++)
        {
            string? next = i + 1 < e.Args.Length ? e.Args[i + 1] : null;
            switch (e.Args[i])
            {
                case "--root" when next is not null: RootOverride = next; i++; break;
                case "--snapshot" when next is not null: SnapshotDir = next; i++; break;
                case "--lang" when next is not null: LanguageOverride = next; i++; break;
                case "--selftest" when next is not null: SelfTestLog = next; i++; break;
                case "--mask-user": MaskUser = true; break;
            }
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, Loc.T("UnexpectedError"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }

    /// <summary>Path for display; with --mask-user the user name is replaced.</summary>
    public static string ShowPath(string? path)
    {
        if (path is null || !MaskUser) return path ?? "";
        // A test copy (--root) is shown at the real save location, so screenshots look like a normal install.
        string? copyParent = RootOverride is null ? null : Path.GetDirectoryName(Path.GetFullPath(RootOverride));
        if (copyParent is not null && path.StartsWith(copyParent, StringComparison.OrdinalIgnoreCase))
            return @"C:\Users\<user>\AppData\LocalLow\LuisDev" + path[copyParent.Length..];
        return path.Replace(@"\Users\" + Environment.UserName + @"\", @"\Users\<user>\", StringComparison.OrdinalIgnoreCase);
    }
}
