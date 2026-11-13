using System.Text.Json;
using System.Text.Json.Serialization;

namespace CanEmAllEditor.Core;

/// <summary>The demo and the full game share the save format but not the skill data.</summary>
public enum GameEdition { Demo, Full }

public static class Editions
{
    public const int DemoAppId = 5364590;
    public const int FullAppId = 5133900;

    public static int AppId(GameEdition e) => e == GameEdition.Demo ? DemoAppId : FullAppId;

    /// <summary>"Can 'Em All Demo" is the demo, everything else ("Can 'Em All") the full game.</summary>
    public static GameEdition FromFolderName(string name) =>
        name.Contains("Demo", StringComparison.OrdinalIgnoreCase) ? GameEdition.Demo : GameEdition.Full;
}

/// <summary>A permanent upgrade (full game): bought with upgrade points from selling the factory, kept across runs.</summary>
public sealed class PermanentDef
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("key")] public string Key { get; init; } = "";
    [JsonPropertyName("cls")] public string Class { get; init; } = "";
    [JsonPropertyName("cost")] public int Cost { get; init; }
    [JsonPropertyName("row")] public int Row { get; init; }
    [JsonPropertyName("col")] public int Col { get; init; }
    [JsonPropertyName("cash")] public int Cash { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("desc")] public string DescriptionEn { get; init; } = "";

    public string Description => Loc.Instance.Language == UiLanguage.German ? Loc.DescriptionDe(DescriptionEn) : DescriptionEn;
}

/// <summary>A story mail (full game). The id is the game's MailId enum value.</summary>
public sealed class MailDef
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("key")] public string Key { get; init; } = "";
    [JsonPropertyName("sender")] public string Sender { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("body")] public string Body { get; init; } = "";

    /// <summary>Mails whose "accept" flag does something: slot machine and the factory sale offer.</summary>
    public bool Acceptable => Key is "FeelingLucky" or "AnOfferYouCantRefuse";

    public string SenderName => Sender switch
    {
        "Corporation" => "Happy Meat Food Corp.",
        "News" => "The Daily Chronicle",
        "Activist" => "C.A.N. (Cuteness Action Network)",
        _ => Loc.T("SenderUnknown"),
    };

    public Rgb SenderColor => Sender switch
    {
        "Corporation" => Rgb.FromRgb(0xF8, 0x71, 0x71),
        "News" => Rgb.FromRgb(0x60, 0xA5, 0xFA),
        "Activist" => Rgb.FromRgb(0x4A, 0xDE, 0x80),
        _ => Rgb.FromRgb(0xFA, 0xCC, 0x15),
    };
}

public static class GameData
{
    public static IReadOnlyList<PermanentDef> Permanent { get; } = Read<PermanentDef>("permanent.json");
    public static IReadOnlyList<MailDef> Mails { get; } = Read<MailDef>("mails.json");

    static List<T> Read<T>(string name)
    {
        using var s = typeof(GameData).Assembly.GetManifestResourceStream(name)
                      ?? throw new InvalidOperationException(name + " resource missing");
        return JsonSerializer.Deserialize<List<T>>(s) ?? [];
    }
}
