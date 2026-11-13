using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace CanEmAllEditor.Core;

/// <summary>One game data folder (demo or full game) with save.sav and settings.sav.</summary>
public sealed class GameFolder
{
    public required string Path { get; init; }
    public required string Title { get; init; }
    public GameEdition Edition => Editions.FromFolderName(Title);
    public string SavePath => System.IO.Path.Combine(Path, "save.sav");
    public string SettingsPath => System.IO.Path.Combine(Path, "settings.sav");
    public string BackupRoot => System.IO.Path.Combine(Path, "EditorBackups");
    public bool HasSave => File.Exists(SavePath);
    public bool HasSettings => File.Exists(SettingsPath);
    public DateTime LastWrite => HasSave ? File.GetLastWriteTime(SavePath) : HasSettings ? File.GetLastWriteTime(SettingsPath) : DateTime.MinValue;

    // Summary for the sidebar (read on scan)
    public long Money { get; set; }
    public long Day { get; set; }
    public int SkillCount { get; set; }
    public long UpgradePoints { get; set; }
}

public sealed class BackupEntry
{
    public required string Folder { get; init; }
    public required DateTime Time { get; init; }
    public long Money { get; init; }
    public long Day { get; init; }
    public int Skills { get; init; }
    public bool HasSave { get; init; }
    public bool HasSettings { get; init; }
    public string Name => System.IO.Path.GetFileName(Folder);
}

public static class SaveStore
{
    public const string Company = "LuisDev";
    public static readonly string[] ProcessNames = ["Can 'Em All Demo", "Can 'Em All"];
    public static readonly string[] SettingsFloatKeys = ["mouseSensitivity", "masterVolume", "musicVolume", "sfxVolume"];
    // fieldOfView, bloodIndex, woundsIndex are ints in the full game

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Windows: %USERPROFILE%\AppData\LocalLow\LuisDev. Linux: the first LuisDev folder found in a Proton prefix.</summary>
    public static string DefaultRoot => OperatingSystem.IsWindows()
        ? System.IO.Path.Combine(Home, "AppData", "LocalLow", Company)
        : LinuxRoots(Home).FirstOrDefault() ?? ProtonRoot(System.IO.Path.Combine(Home, ".local", "share", "Steam"), Editions.FullAppId);

    /// <summary>Every "Can 'Em All*" folder (full game first). --root uses that folder directly.</summary>
    public static List<GameFolder> Scan(string? rootOverride)
    {
        var result = new List<GameFolder>();
        if (rootOverride is not null)
        {
            result.Add(Describe(rootOverride));
            return result;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in OperatingSystem.IsWindows() ? [DefaultRoot] : LinuxRoots(Home))
            if (Directory.Exists(root))
                foreach (var dir in Directory.GetDirectories(root, "Can*"))
                    if (seen.Add(System.IO.Path.GetFullPath(dir))) result.Add(Describe(dir));
        if (result.Count == 0)
            result.Add(Describe(System.IO.Path.Combine(DefaultRoot, "Can 'Em All")));
        return result.OrderBy(f => f.Edition == GameEdition.Demo).ThenBy(f => f.Title).ToList();
    }

    // ---- Linux: the game runs through Proton, its saves live inside the Wine prefix of each app id

    /// <summary>Steam installs: native, Flatpak and Snap.</summary>
    static IEnumerable<string> SteamRoots(string home) =>
    [
        System.IO.Path.Combine(home, ".steam", "steam"),
        System.IO.Path.Combine(home, ".steam", "root"),
        System.IO.Path.Combine(home, ".local", "share", "Steam"),
        System.IO.Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
        System.IO.Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
        System.IO.Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam"),
    ];

    /// <summary>Every Steam library: the Steam folders plus the "path" entries of libraryfolders.vdf (other drives, SD card).</summary>
    static IEnumerable<string> SteamLibraries(string home)
    {
        foreach (var root in SteamRoots(home).Where(Directory.Exists))
        {
            yield return root;
            string vdf = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            string[] lines;
            try { lines = File.ReadAllLines(vdf); } catch (Exception) { continue; }
            foreach (var line in lines)
            {
                var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(p => p.Length > 0).ToArray();
                if (parts.Length >= 2 && parts[0] == "path") yield return parts[1].Replace(@"\\", @"\");
            }
        }
    }

    /// <summary>Path with every symlink resolved (~/.steam/steam usually links to ~/.local/share/Steam), so a folder is listed once.</summary>
    static string RealPath(string path)
    {
        string cur = "/";
        foreach (var part in System.IO.Path.GetFullPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            cur = System.IO.Path.Combine(cur, part);
            try { if (new DirectoryInfo(cur).ResolveLinkTarget(true) is { } target) cur = target.FullName; }
            catch (Exception) { /* keep as is */ }
        }
        return cur;
    }

    static string ProtonRoot(string library, int appId) => System.IO.Path.Combine(library, "steamapps", "compatdata",
        appId.ToString(CultureInfo.InvariantCulture), "pfx", "drive_c", "users", "steamuser", "AppData", "LocalLow", Company);

    /// <summary>All existing LuisDev folders on Linux: Proton prefixes of the full game and the demo, plus a native Unity build.</summary>
    public static List<string> LinuxRoots(string home)
    {
        var roots = new List<string>();
        void Add(string dir)
        {
            if (!Directory.Exists(dir)) return;
            string real = RealPath(dir);
            if (!roots.Contains(real)) roots.Add(real);
        }
        foreach (var lib in SteamLibraries(home))
            foreach (int id in new[] { Editions.FullAppId, Editions.DemoAppId })
                Add(ProtonRoot(lib, id));
        Add(System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : System.IO.Path.Combine(home, ".config"), "unity3d", Company));
        return roots;
    }

    static GameFolder Describe(string dir)
    {
        var f = new GameFolder { Path = dir, Title = System.IO.Path.GetFileName(dir.TrimEnd('\\', '/')) };
        try
        {
            if (f.HasSave)
            {
                var j = UnityJson.Parse(File.ReadAllText(f.SavePath));
                f.Money = j.GetLong("money");
                f.Day = j.GetLong("day");
                f.SkillCount = j.GetIntList("unlockedSkills").Count;
                f.UpgradePoints = j.GetLong("upgradePoints");
            }
        }
        catch (Exception) { /* summary only */ }
        return f;
    }

    public static bool IsGameRunning()
    {
        if (!OperatingSystem.IsWindows()) return IsGameRunningLinux();
        foreach (var name in ProcessNames)
        {
            var procs = Process.GetProcessesByName(name);
            bool running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            if (running) return true;
        }
        return false;
    }

    /// <summary>Under Proton the game is a Wine process: look for its exe in every command line.</summary>
    static bool IsGameRunningLinux()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!char.IsDigit(System.IO.Path.GetFileName(dir)[0])) continue;
                string cmd;
                try { cmd = File.ReadAllText(System.IO.Path.Combine(dir, "cmdline")); } catch (Exception) { continue; }
                foreach (var name in ProcessNames)
                    if (cmd.Contains(name + ".exe", StringComparison.OrdinalIgnoreCase) || cmd.Contains(name + ".x86_64", StringComparison.OrdinalIgnoreCase))
                        return true;
            }
        }
        catch (Exception) { /* no /proc: assume not running */ }
        return false;
    }

    /// <summary>Copies the current files into EditorBackups\yyyy-MM-dd_HH-mm-ss and returns that folder.</summary>
    public static string? CreateBackup(GameFolder folder, string suffix = "")
    {
        if (!folder.HasSave && !folder.HasSettings) return null;
        string name = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + suffix;
        string dest = System.IO.Path.Combine(folder.BackupRoot, name);
        int n = 1;
        while (Directory.Exists(dest)) dest = System.IO.Path.Combine(folder.BackupRoot, $"{name}_{n++}");
        Directory.CreateDirectory(dest);
        if (folder.HasSave) File.Copy(folder.SavePath, System.IO.Path.Combine(dest, "save.sav"));
        if (folder.HasSettings) File.Copy(folder.SettingsPath, System.IO.Path.Combine(dest, "settings.sav"));
        return dest;
    }

    public static List<BackupEntry> ListBackups(GameFolder folder)
    {
        var list = new List<BackupEntry>();
        if (!Directory.Exists(folder.BackupRoot)) return list;
        foreach (var dir in Directory.GetDirectories(folder.BackupRoot))
        {
            string save = System.IO.Path.Combine(dir, "save.sav");
            long money = 0, day = 0; int skills = 0;
            try
            {
                if (File.Exists(save))
                {
                    var j = UnityJson.Parse(File.ReadAllText(save));
                    money = j.GetLong("money"); day = j.GetLong("day"); skills = j.GetIntList("unlockedSkills").Count;
                }
            }
            catch (Exception) { /* show without summary */ }
            list.Add(new BackupEntry
            {
                Folder = dir,
                Time = Directory.GetCreationTime(dir),
                Money = money, Day = day, Skills = skills,
                HasSave = File.Exists(save),
                HasSettings = File.Exists(System.IO.Path.Combine(dir, "settings.sav")),
            });
        }
        return list.OrderByDescending(b => b.Time).ToList();
    }

    public static void RestoreBackup(GameFolder folder, BackupEntry backup)
    {
        Directory.CreateDirectory(folder.Path);
        string save = System.IO.Path.Combine(backup.Folder, "save.sav");
        string settings = System.IO.Path.Combine(backup.Folder, "settings.sav");
        if (File.Exists(save)) File.Copy(save, folder.SavePath, true);
        if (File.Exists(settings)) File.Copy(settings, folder.SettingsPath, true);
    }

    /// <summary>Write through a temp file so a crash never leaves a half-written save.</summary>
    public static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string tmp = path + ".editortmp";
        File.WriteAllText(tmp, content, new System.Text.UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}
