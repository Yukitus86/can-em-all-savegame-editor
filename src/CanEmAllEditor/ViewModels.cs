using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CanEmAllEditor.Core;

namespace CanEmAllEditor;

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
    public string IconUri => Def.IconUri;
    public bool DemoLocked => Def.DemoLocked;
    public bool Special => Def.Special;
    public string CostText => Loc.Num(Def.Cost);
    public string CostShort => Loc.Short(Def.Cost);
    public string CategoryLabel => SkillData.LabelOf(Def.Category);
    public string Meta => Loc.T("SkillIdN", Def.Id, SkillData.LabelOf(Def.Category));
    public SolidColorBrush CategoryBrush { get; } = Frozen(SkillData.ColorOf(def.Category).Wpf());
    public SolidColorBrush CategorySoft { get; } = Frozen(Color.FromArgb(0x38, SkillData.ColorOf(def.Category).R, SkillData.ColorOf(def.Category).G, SkillData.ColorOf(def.Category).B));

    public bool Owned => isOwned(Def.Id);
    public bool Available => !Owned && Def.Requirements.All(isOwned);

    public void Refresh() => RaiseAll();

    static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

public sealed class CategoryVM : Observable
{
    public SkillCategory Category { get; init; }
    public string Label => SkillData.LabelOf(Category);
    public SolidColorBrush Brush { get; init; } = Brushes.White;
    public int Total { get; set; }
    public int OwnedCount { get; set; }
    public double Percent => Total == 0 ? 0 : 100.0 * OwnedCount / Total;
    public string CountText => $"{OwnedCount} / {Total}";
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
    public string Meta => Loc.T("SkillIdN", Def.Id, Loc.T("Permanent"));
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
    public SolidColorBrush SenderBrush { get; } = Frozen(def.SenderColor.Wpf());

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

    static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
