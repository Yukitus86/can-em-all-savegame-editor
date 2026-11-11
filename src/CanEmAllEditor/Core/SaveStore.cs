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

    public static string DefaultRoot => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", Company);

    /// <summary>Every "Can 'Em All*" folder under LocalLow\LuisDev (full game first). --root uses that folder directly.</summary>
    public static List<GameFolder> Scan(string? rootOverride)
    {
        var result = new List<GameFolder>();
        if (rootOverride is not null)
        {
            result.Add(Describe(rootOverride));
            return result;
        }
        if (Directory.Exists(DefaultRoot))
            foreach (var dir in Directory.GetDirectories(DefaultRoot, "Can*"))
                result.Add(Describe(dir));
        if (result.Count == 0)
            result.Add(Describe(System.IO.Path.Combine(DefaultRoot, "Can 'Em All")));
        return result.OrderBy(f => f.Edition == GameEdition.Demo).ThenBy(f => f.Title).ToList();
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
        foreach (var name in ProcessNames)
        {
            var procs = Process.GetProcessesByName(name);
            bool running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            if (running) return true;
        }
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
