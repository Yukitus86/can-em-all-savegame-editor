using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CanEmAllEditor.Core;

namespace CanEmAllEditor;

/// <summary>Bridges the UI-neutral core types to Avalonia.</summary>
static class Ui
{
    public static Color Av(this Rgb c) => Color.FromRgb(c.R, c.G, c.B);
    public static IBrush Brush(this Rgb c) => new ImmutableSolidColorBrush(c.Av());
    public static IBrush Brush(this Rgb c, byte alpha) => new ImmutableSolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));

    static readonly Dictionary<string, Bitmap> icons = [];

    /// <summary>A game icon from Assets/icons, decoded small and cached.</summary>
    public static Bitmap Icon(string name)
    {
        if (icons.TryGetValue(name, out var bmp)) return bmp;
        using var s = AssetLoader.Open(new Uri($"avares://CanEmAllEditor/Assets/icons/{name}.png"));
        return icons[name] = Bitmap.DecodeToWidth(s, 96, BitmapInterpolationMode.HighQuality);
    }
}

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>One skill as shown in the list and the tree detail panel.</summary>
public sealed class SkillVM(SkillDef def, Func<int, bool> isOwned) : Observable
{
    public SkillDef Def { get; } = def;
    public int Id => Def.Id;
    public string Name => Def.Name;
    public string Description => Def.Description;
    public Bitmap Icon => Ui.Icon(Def.Icon);
    public bool DemoLocked => Def.DemoLocked;
    public bool Special => Def.Special;
    public string CostText => "$ " + Loc.Num(Def.Cost);
    public string Meta => Loc.T("SkillIdN", Def.Id, SkillData.LabelOf(Def.Category));
    public string DemoLabel => Loc.T("DemoLocked");
    public IBrush CategoryBrush { get; } = SkillData.ColorOf(def.Category).Brush();
    public IBrush CategorySoft { get; } = SkillData.ColorOf(def.Category).Brush(0x38);

    public bool Owned => isOwned(Def.Id);
    public bool Available => !Owned && Def.Requirements.All(isOwned);

    /// <summary>Filter state of the skill list (cards stay created, hidden ones collapse).</summary>
    public bool Shown { get; set; } = true;

    public void Refresh() => RaiseAll();
}

public sealed class CategoryVM : Observable
{
    public SkillCategory Category { get; init; }
    public string Label => SkillData.LabelOf(Category);
    public IBrush Brush { get; init; } = Brushes.White;
    public int Total { get; set; }
    public int OwnedCount { get; set; }
    public double Percent => Total == 0 ? 0 : 100.0 * OwnedCount / Total;
    public string CountText => $"{OwnedCount} / {Total}";
    public string OnText => Loc.T("CatOn");
    public string OffText => Loc.T("CatOff");
    public void Refresh() => RaiseAll();
}

/// <summary>One permanent upgrade card (full game).</summary>
public sealed class PermanentVM(PermanentDef def, Func<int, bool> isOwned) : Observable
{
    public PermanentDef Def { get; } = def;
    public int Id => Def.Id;
    public string Name => Def.Name;
    public string Description => Def.Description;
    public string CostText => Loc.T("PointsN", Def.Cost);
    public bool Owned => isOwned(Def.Id);
    public void Refresh() => RaiseAll();
}

/// <summary>One story mail with its state in the save (full game).</summary>
public sealed class MailVM(MailDef def) : Observable
{
    public MailDef Def { get; } = def;
    public int Id => Def.Id;
    public string Title => Def.Title;
    public string SenderName => Def.SenderName;
    public IBrush SenderBrush { get; } = def.SenderColor.Brush();

    // state, filled from the save
    public bool Received { get; set; }
    public bool Opened { get; set; }
    public bool Accepted { get; set; }
    public long ReceivedDay { get; set; }

    public string StateIcon => !Received ? "·" : Opened ? "✓" : "✉";
    public string StateText => !Received ? Loc.T("MailNotReceived")
        : (Opened ? Loc.T("MailRead") : Loc.T("MailUnread")) + "  ·  " + Loc.T("MailDayN", ReceivedDay)
          + (Accepted ? "  ·  " + Loc.T("MailAccepted") : "");
    public double RowOpacity => Received ? 1.0 : 0.45;

    public void Refresh() => RaiseAll();
}

public sealed record FolderItem(GameFolder Folder, string Title, string MoneyText, string SubText);
public sealed record BackupItem(BackupEntry Entry, string Title, string Line);
