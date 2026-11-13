using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CanEmAllEditor.Core;

/// <summary>Small per-user settings file: %APPDATA%\CanEmAllEditor\settings.json (Linux: ~/.config/CanEmAllEditor).</summary>
public sealed class AppSettings
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CanEmAllEditor", "settings.json");

    public string? Language { get; set; }
    public bool AutoRequirements { get; set; } = true;
    public bool AllowDemoLocked { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception) { /* broken settings file: defaults */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception) { /* not worth an error dialog */ }
    }

    /// <summary>Saved choice, otherwise German on a German Windows, else English.</summary>
    public UiLanguage ResolveLanguage() => Language switch
    {
        "de" => UiLanguage.German,
        "en" => UiLanguage.English,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? UiLanguage.German : UiLanguage.English,
    };

    public static string Code(UiLanguage language) => language == UiLanguage.German ? "de" : "en";
}
