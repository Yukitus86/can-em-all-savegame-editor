using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CanEmAllEditor.Core;

public enum SkillCategory { Combat, Weapons, Factory, Vacuum, Movement, Creatures, Robots, Turrets, Special }

/// <summary>
/// One node of the in-game skill tree. Extracted from the game's scene (SkillNode + Skill components):
/// id is what the save stores in "unlockedSkills", x/y is the node position in the tree canvas.
/// </summary>
public sealed class SkillDef
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("key")] public string Key { get; init; } = "";
    [JsonPropertyName("cls")] public string Class { get; init; } = "";
    [JsonPropertyName("cost")] public long Cost { get; init; }
    [JsonPropertyName("tag")] public int Tag { get; init; }
    [JsonPropertyName("demo")] public bool DemoLocked { get; init; }
    [JsonPropertyName("special")] public bool Special { get; init; }
    [JsonPropertyName("req")] public int[] Requirements { get; init; } = [];
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
    [JsonPropertyName("icon")] public string Icon { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("desc")] public string DescriptionEn { get; init; } = "";

    [JsonIgnore] public SkillCategory Category { get; set; }
    [JsonIgnore] public List<SkillDef> Unlocks { get; } = [];

    public string Description => Loc.Instance.Language == UiLanguage.German ? Loc.DescriptionDe(DescriptionEn) : DescriptionEn;
}

public static class SkillData
{
    public static IReadOnlyList<SkillDef> All { get; private set; } = [];
    public static IReadOnlyDictionary<int, SkillDef> ById { get; private set; } = new Dictionary<int, SkillDef>();
    public static GameEdition Edition { get; private set; }

    /// <summary>Categories that have at least one skill in the loaded edition (display order).</summary>
    public static SkillCategory[] Categories { get; private set; } = [];

    static SkillData() => Load(GameEdition.Full);

    /// <summary>Loads the skill tree of the demo or the full game (prices, layout and skills differ).</summary>
    public static void Load(GameEdition edition)
    {
        string res = edition == GameEdition.Demo ? "skills_demo.json" : "skills_full.json";
        using var s = typeof(SkillData).Assembly.GetManifestResourceStream(res)
                      ?? throw new InvalidOperationException(res + " resource missing");
        var list = JsonSerializer.Deserialize<List<SkillDef>>(s) ?? [];
        foreach (var d in list) d.Category = Categorize(d);
        var byId = list.ToDictionary(d => d.Id);
        foreach (var d in list)
            foreach (int r in d.Requirements)
                if (byId.TryGetValue(r, out var req)) req.Unlocks.Add(d);
        All = list;
        ById = byId;
        Edition = edition;
        Categories = AllCategories.Where(c => list.Any(d => d.Category == c)).ToArray();
    }

    static SkillCategory Categorize(SkillDef d) => d.Class switch
    {
        "Skill" => SkillCategory.Special,
        "UnlockWeapon" when d.Key == "Vacuum" => SkillCategory.Vacuum,
        "UnlockWeapon" or "ShotgunSpreadSkill" or "RocketLauncherExplosionRadiusSkill" or "SmokedSkill" => SkillCategory.Weapons,
        "PunchDamageSkill" or "PunchSpeedSkill" or "AttackSpeedSkill" or "DamageSkill" or "CriticalDamageSkill"
            or "ExplosiveBulletSkill" or "ExplosiveBulletDamageSkill" or "ShootSpeedSkill" or "FreeBulletProbability"
            or "BleedingDurationSkill" or "MoreBulletSkill" or "ShockwaveSkill" or "ShockwaveDamageSkill"
            or "InstantKillSkill" => SkillCategory.Combat,
        "MoreCansSkill" or "ExtraCanChanceSkill" or "CanValueMultiplierSkill" or "ShinyCanChanceSkill" or "ShinyValueSkill"
            or "DuplicateMoneyChanceSkill" or "CanProcessingSpeedSkill" or "ConveyorSpeedSkill" or "UnlockMachineSkill"
            or "UnlockConveyorSkill" => SkillCategory.Factory,
        "VacuumShootSpeedSkill" or "VacuumRangeSkill" or "VacuumShootDistanceSkill" or "HoleInOneSkill"
            or "VacuumStorageSkill" or "VacuumDuplicateStorageSkill" => SkillCategory.Vacuum,
        "ThrowSkill" or "StaminaSkill" or "InfiniteStamina" or "MoveSpeedSkill" => SkillCategory.Movement,
        "UnlockRobotSkill" or "RobotPusherSkill" or "RobotSpeedSkill" => SkillCategory.Robots,
        "UnlockTurretSkill" or "TurretDamageSkill" or "TurretBulletSkill" => SkillCategory.Turrets,
        _ => SkillCategory.Creatures,
    };

    static readonly SkillCategory[] AllCategories =
        [SkillCategory.Combat, SkillCategory.Weapons, SkillCategory.Turrets, SkillCategory.Factory, SkillCategory.Vacuum,
         SkillCategory.Robots, SkillCategory.Movement, SkillCategory.Creatures, SkillCategory.Special];

    public static Rgb ColorOf(SkillCategory c) => c switch
    {
        SkillCategory.Combat => Rgb.FromRgb(0xF8, 0x71, 0x71),
        SkillCategory.Weapons => Rgb.FromRgb(0xFB, 0x92, 0x3C),
        SkillCategory.Factory => Rgb.FromRgb(0x34, 0xD3, 0x99),
        SkillCategory.Vacuum => Rgb.FromRgb(0x38, 0xBD, 0xF8),
        SkillCategory.Movement => Rgb.FromRgb(0xFA, 0xCC, 0x15),
        SkillCategory.Creatures => Rgb.FromRgb(0xC0, 0x84, 0xFC),
        SkillCategory.Robots => Rgb.FromRgb(0xCB, 0xD5, 0xE1),
        SkillCategory.Turrets => Rgb.FromRgb(0xF4, 0x72, 0xB6),
        _ => Rgb.FromRgb(0xFF, 0xD3, 0x6B),
    };

    public static string LabelOf(SkillCategory c) => Loc.T("Cat" + c);

    /// <summary>The skill plus everything it needs, prerequisites first (purchase order).</summary>
    public static List<int> WithRequirements(int id)
    {
        var order = new List<int>();
        var seen = new HashSet<int>();
        void Visit(int x)
        {
            if (!seen.Add(x) || !ById.TryGetValue(x, out var d)) return;
            foreach (int r in d.Requirements) Visit(r);
            order.Add(x);
        }
        Visit(id);
        return order;
    }

    /// <summary>The skill plus every skill that (transitively) builds on it.</summary>
    public static HashSet<int> WithDependents(int id)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            int x = stack.Pop();
            if (!seen.Add(x) || !ById.TryGetValue(x, out var d)) continue;
            foreach (var u in d.Unlocks) stack.Push(u.Id);
        }
        return seen;
    }

    /// <summary>All ids in a valid purchase order (every prerequisite before its skill).</summary>
    public static List<int> TopologicalOrder(IEnumerable<int> ids)
    {
        var want = ids.ToHashSet();
        var order = new List<int>();
        var seen = new HashSet<int>();
        void Visit(int x)
        {
            if (!seen.Add(x) || !ById.TryGetValue(x, out var d)) return;
            foreach (int r in d.Requirements) if (want.Contains(r)) Visit(r);
            order.Add(x);
        }
        foreach (var d in All) if (want.Contains(d.Id)) Visit(d.Id);
        return order;
    }
}
