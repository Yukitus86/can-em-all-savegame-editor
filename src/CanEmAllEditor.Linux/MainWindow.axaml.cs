using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CanEmAllEditor.Core;
using Path = System.IO.Path;

namespace CanEmAllEditor;

/// <summary>
/// The Linux editor window. Same layout and behaviour as the Windows (WPF) editor,
/// built on Avalonia; all save logic lives in CanEmAllEditor.Core.
/// </summary>
public partial class MainWindow : Window
{
    const long MoneyMax = int.MaxValue;

    readonly AppSettings appSettings = AppSettings.Load();
    SkillTreeControl tree = null!;
    List<SkillVM> skillVms = [];
    List<CategoryVM> categoryVms = [];
    GameEdition edition = SkillData.Edition;
    bool IsFull => edition == GameEdition.Full;

    // full game: permanent upgrades + mails
    readonly List<PermanentVM> permVms;
    readonly List<int> permOrder = [];
    readonly HashSet<int> permOwned = [];
    readonly List<MailVM> mailVms;
    readonly DispatcherTimer runningTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    GameFolder? folder;
    UnityJson? save;                    // null = no save.sav on disk
    UnityJson settings = UnityJson.Empty();
    bool settingsExists;
    string savedSaveText = "", savedSettingsText = "";
    DateTime saveStamp, settingsStamp;
    readonly List<int> owned = [];
    readonly HashSet<int> ownedSet = [];
    bool loading;
    bool dirty;
    bool gameRunning;
    bool ready;                         // false while InitializeComponent builds the controls
    bool closeConfirmed;
    SkillCategory? chipFilter;

    public MainWindow()
    {
        InitializeComponent();
        Loc.Instance.SetLanguage(App.LanguageOverride switch
        {
            "de" => UiLanguage.German,
            "en" => UiLanguage.English,
            _ => appSettings.ResolveLanguage(),
        });
        VersionText.Text = "v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0") + "  ·  Yukitus86  ·  Linux";

        AutoReqSwitch.IsChecked = appSettings.AutoRequirements;
        AllowDemoSwitch.IsChecked = appSettings.AllowDemoLocked;

        BuildSkillModels();

        permVms = GameData.Permanent.Select(d => new PermanentVM(d, permOwned.Contains)).ToList();
        PermList.ItemsSource = permVms;
        mailVms = GameData.Mails.Select(d => new MailVM(d)).ToList();

        // Text changes are handled on the property itself: TextBox.TextChanged is raised later than the change,
        // so a value typed and saved right away (or set from code) would be missed.
        foreach (var tb in NumberBoxes) OnText(tb, () => StatBox_Changed(tb));
        OnText(SearchBox, () => { if (ready) RefreshList(); });
        OnText(MailDayBox, MailDay_Changed);
        OnText(ResolutionBox, () => Setting_Changed(ResolutionBox, new RoutedEventArgs()));

        ready = true;
        BuildLocalizedLists();
        ApplyEditionUi();
        Loc.Instance.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Loc.Language)) OnLanguageChanged(); };
        UpdateFlags();

        runningTimer.Tick += (_, _) => UpdateRunningState();
        runningTimer.Start();
        UpdateRunningState();

        Closing += OnClosing;
        Opened += async (_, _) =>
        {
            ScanFolders();
            if (App.SnapshotDir is not null) await RunSnapshots(App.SnapshotDir);
            else if (App.SelfTestLog is not null) { await Task.Delay(300); await RunSelfTest(App.SelfTestLog); }
        };
    }

    IBrush Res(string key) => this.TryFindResource(key, out var v) && v is IBrush b ? b : Brushes.Gray;

    static void SetBad(Control c, bool bad) => c.Classes.Set("bad", bad);

    static void OnText(TextBox box, Action changed) =>
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) changed(); };

    // ===================================================================== folders / loading

    void ScanFolders(string? keepPath = null)
    {
        keepPath ??= folder?.Path;
        var folders = SaveStore.Scan(App.RootOverride);
        var items = folders.Select(f => new FolderItem(f, f.Title,
            f.HasSave ? "$ " + Loc.Num(f.Money) : Loc.T("NoSave"),
            f.HasSave ? Loc.T("DayN", f.Day) + "  ·  " + Loc.T("SkillsN", f.SkillCount)
                        + (f.Edition == GameEdition.Full ? "  ·  ◆ " + Loc.Num(f.UpgradePoints) : "") : "")).ToList();
        var pick = items.FirstOrDefault(i => i.Folder.Path == keepPath) ?? items.FirstOrDefault();
        loading = true;
        FolderList.ItemsSource = items;
        FolderList.SelectedItem = pick;
        loading = false;
        if (pick is not null && folder?.Path != pick.Folder.Path) LoadFolder(pick.Folder);
    }

    async void FolderList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ready || loading || FolderList.SelectedItem is not FolderItem item) return;
        if (folder?.Path == item.Folder.Path) return;
        if (dirty && !await ConfirmDiscard())
        {
            loading = true;
            FolderList.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
            loading = false;
            return;
        }
        LoadFolder(item.Folder);
    }

    /// <summary>Skill tree, list and category models for the loaded edition.</summary>
    void BuildSkillModels()
    {
        tree = new SkillTreeControl { IsOwned = ownedSet.Contains };
        tree.NodeSelected += id => ShowDetail(id);
        tree.NodeToggled += id => { ToggleSkill(id); ShowDetail(id); };
        TreeHost.Child = tree;

        skillVms = SkillData.All.Select(d => new SkillVM(d, ownedSet.Contains)).ToList();
        SkillList.ItemsSource = skillVms;

        categoryVms = SkillData.Categories.Select(c => new CategoryVM
        {
            Category = c,
            Brush = SkillData.ColorOf(c).Brush(),
            Total = SkillData.All.Count(s => s.Category == c),
        }).ToList();
        CategoryList.ItemsSource = categoryVms;
    }

    void SwitchEdition(GameEdition e)
    {
        SkillData.Load(e);
        edition = e;
        chipFilter = null;
        BuildSkillModels();
        DetailPanel.IsVisible = false;
        DetailEmpty.IsVisible = true;
        bool was = loading;
        loading = true;
        BuildLocalizedLists();
        loading = was;
        ApplyEditionUi();
    }

    /// <summary>Full game only: prestige + mails tabs, new settings. Demo only: demo-lock options.</summary>
    void ApplyEditionUi()
    {
        PrestigeTab.IsVisible = MailsTab.IsVisible = IsFull;
        HeaderPointsPanel.IsVisible = EverythingButton.IsVisible = FullSettingsPanel.IsVisible = IsFull;
        DemoCountPanel.IsVisible = DemoOptions.IsVisible = DemoLegend.IsVisible = !IsFull;
        HeaderEditionText.Text = Loc.T(IsFull ? "EditionFull" : "EditionDemo").ToUpper(Loc.Instance.Culture);
        if (Tabs.SelectedItem is TabItem { IsVisible: false }) Tabs.SelectedIndex = 0;
    }

    void LoadFolder(GameFolder f)
    {
        if (f.Edition != edition) SwitchEdition(f.Edition);
        folder = f;
        try
        {
            if (f.HasSave)
            {
                savedSaveText = File.ReadAllText(f.SavePath);
                save = UnityJson.Parse(savedSaveText);
                saveStamp = File.GetLastWriteTimeUtc(f.SavePath);
            }
            else
            {
                save = null;
                savedSaveText = "";
            }

            settingsExists = f.HasSettings;
            if (settingsExists)
            {
                savedSettingsText = File.ReadAllText(f.SettingsPath);
                settings = UnityJson.Parse(savedSettingsText, SaveStore.SettingsFloatKeys);
                settingsStamp = File.GetLastWriteTimeUtc(f.SettingsPath);
            }
            else
            {
                savedSettingsText = "";
                settings = DefaultSettings();
            }
        }
        catch (Exception ex)
        {
            save = null;
            settings = DefaultSettings();
            SetStatus(Loc.T("LoadFailed", ex.Message), StatusKind.Bad);
            PopulateAll();
            return;
        }

        PopulateAll();
        SetDirty(false);
        SetStatus(Loc.T("Loaded", f.Title), StatusKind.Good);
        RefreshBackups();
    }

    UnityJson DefaultSettings()
    {
        var s = UnityJson.Empty(SaveStore.SettingsFloatKeys);
        s.SetLong("languageIndex", 0);
        s.SetDouble("mouseSensitivity", 1.0);
        if (IsFull)
        {
            s.SetLong("fieldOfView", 60);
            s.SetBool("invertY", false);
            s.SetBool("headBob", true);
            s.SetLong("bloodIndex", 0);
            s.SetLong("woundsIndex", 0);
        }
        s.SetDouble("masterVolume", 100);
        s.SetDouble("musicVolume", 100);
        s.SetDouble("sfxVolume", 100);
        s.SetLong("windowModeIndex", 1);
        s.SetLong("resolutionIndex", 0);
        s.SetBool("vSync", true);
        s.SetLong("antialiasingIndex", 0);
        s.SetBool("shadows", true);
        s.SetLong("reflectionsIndex", 1);
        s.SetBool("ambientOcclusion", true);
        s.SetBool("bloom", true);
        s.SetBool("chromaticAberration", true);
        return s;
    }

    UnityJson NewSave()
    {
        var s = UnityJson.Empty();
        s.SetLong("money", 0);
        s.SetLong("day", 1);
        s.SetLong("totalKilled", 0);
        s.SetLong("totalCans", 0);
        if (IsFull)
        {
            s.SetLong("totalMoney", 0);
            s.SetLong("upgradePoints", 0);
        }
        s.SetIntList("unlockedSkills", []);
        if (IsFull)
        {
            s.SetIntList("unlockedPermanentUpgrades", []);
            s.Array("mails");
            foreach (var k in LifetimeKeys) s.SetPath("lifetimeStats." + k, 0);
            s.SetBool("hasSeenEnding", false);
        }
        return s;
    }

    static readonly string[] LifetimeKeys = ["totalKilled", "totalCans", "totalMoneyEarned", "totalGambled", "totalGamblingWinnings", "totalDaysWorked", "totalFactoriesSold"];

    /// <summary>Every number box; its Tag is the save path it edits.</summary>
    IEnumerable<TextBox> NumberBoxes =>
        [MoneyBox, DayBox, KilledBox, CansBox, PointsBox, RunMoneyBox, LtFactoriesBox, LtKilledBox, LtCansBox, LtMoneyBox, LtDaysBox, LtGambledBox, LtWinBox];

    void PopulateAll()
    {
        loading = true;
        owned.Clear();
        ownedSet.Clear();
        if (save is not null)
            foreach (int id in save.GetIntList("unlockedSkills"))
                if (ownedSet.Add(id)) owned.Add(id);

        permOrder.Clear();
        permOwned.Clear();
        if (save is not null)
            foreach (int id in save.GetIntList("unlockedPermanentUpgrades"))
                if (permOwned.Add(id)) permOrder.Add(id);

        var s = save ?? NewSave();
        foreach (var tb in NumberBoxes)
        {
            tb.Text = s.GetPath((string)tb.Tag!).ToString(CultureInfo.InvariantCulture);
            SetBad(tb, false);
        }
        EndingSwitch.IsChecked = s.GetBool("hasSeenEnding");
        NoSaveBanner.IsVisible = save is null;
        LoadMailStates();

        PopulateSettings();
        loading = false;

        RefreshSkills();
        RefreshPermanent();
        RefreshMails();
        RefreshHeader();
    }

    // ===================================================================== header / status

    enum StatusKind { Neutral, Good, Warn, Bad }

    void SetStatus(string text, StatusKind kind = StatusKind.Neutral)
    {
        StatusText.Text = text;
        StatusDot.Fill = kind switch
        {
            StatusKind.Good => Res("Good"),
            StatusKind.Warn => Res("Warn"),
            StatusKind.Bad => Res("Bad"),
            _ => Res("Muted"),
        };
    }

    void SetDirty(bool value)
    {
        dirty = value;
        DiscardButton.IsEnabled = dirty;
        SaveButton.IsEnabled = dirty && !gameRunning;
        if (dirty) SetStatus(Loc.T("Unsaved"), StatusKind.Warn);
        Title = (dirty ? "● " : "") + "Can 'Em All – Savegame Editor";
    }

    void MarkDirty()
    {
        if (loading) return;
        SetDirty(true);
        RefreshHeader();
    }

    void RefreshHeader()
    {
        HeaderTitle.Text = folder?.Title ?? "Can 'Em All";
        HeaderPath.Text = App.ShowPath(folder?.Path);
        var s = save ?? NewSave();
        HeaderDay.Text = s.GetLong("day").ToString("N0", Loc.Instance.Culture);
        HeaderMoney.Text = "$ " + Loc.Num(s.GetLong("money"));
        HeaderSkills.Text = $"{owned.Count} / {SkillData.All.Count}";
        HeaderPoints.Text = "◆ " + Loc.Num(s.GetLong("upgradePoints"));
        HeaderLastSaved.Text = folder is not null && folder.LastWrite > DateTime.MinValue
            ? folder.LastWrite.ToString("g", Loc.Instance.Culture)
            : "—";
    }

    void UpdateRunningState()
    {
        bool running = SaveStore.IsGameRunning();
        if (running == gameRunning && RunningBanner.IsVisible == running) return;
        gameRunning = running;
        RunningBanner.IsVisible = running;
        SaveButton.IsEnabled = dirty && !gameRunning;
    }

    // ===================================================================== language

    void FlagEn_Click(object? sender, RoutedEventArgs e) => SwitchLanguage(UiLanguage.English);
    void FlagDe_Click(object? sender, RoutedEventArgs e) => SwitchLanguage(UiLanguage.German);

    void SwitchLanguage(UiLanguage lang)
    {
        Loc.Instance.SetLanguage(lang);
        appSettings.Language = AppSettings.Code(lang);
        appSettings.Save();
    }

    void UpdateFlags()
    {
        FlagEn.Classes.Set("active", Loc.Instance.Language == UiLanguage.English);
        FlagDe.Classes.Set("active", Loc.Instance.Language == UiLanguage.German);
    }

    void OnLanguageChanged()
    {
        UpdateFlags();
        bool wasLoading = loading;
        loading = true;
        BuildLocalizedLists();
        PopulateSettings();
        loading = wasLoading;
        foreach (var vm in skillVms) vm.Refresh();
        foreach (var c in categoryVms) c.Refresh();
        HeaderEditionText.Text = Loc.T(IsFull ? "EditionFull" : "EditionDemo").ToUpper(Loc.Instance.Culture);
        RefreshSkills();
        RefreshPermanent();
        RefreshMails();
        if (MailList.SelectedItem is MailVM mv) ShowMail(mv);
        RefreshHeader();
        ScanFolders();
        RefreshBackups();
        if (tree.SelectedId is int id) ShowDetail(id);
        if (dirty) SetStatus(Loc.T("Unsaved"), StatusKind.Warn);
    }

    void BuildLocalizedLists()
    {
        SetItems(StatusFilter, IsFull
            ? [Loc.T("StatusAll"), Loc.T("StatusOwned"), Loc.T("StatusMissing")]
            : [Loc.T("StatusAll"), Loc.T("StatusOwned"), Loc.T("StatusMissing"), Loc.T("StatusDemo")]);
        SetItems(BloodCombo, [Loc.T("Red"), Loc.T("Green"), Loc.T("Blue")]);
        SetItems(WoundsCombo, [Loc.T("Red"), Loc.T("Green"), Loc.T("Blue")]);
        SetItems(MailFilter, [Loc.T("MailFilterAll"), Loc.T("MailFilterReceived"), Loc.T("MailFilterMissing"), Loc.T("MailFilterUnread")]);
        SetItems(GameLangCombo, ["English", "Español"]);
        SetItems(WindowModeCombo, [Loc.T("WmExclusive"), Loc.T("WmFullscreen"), Loc.T("WmMaximized"), Loc.T("WmWindowed")]);
        SetItems(AACombo, ["SMAA", "FXAA", Loc.T("Off")]);
        SetItems(ReflCombo, [Loc.T("RefAll"), Loc.T("RefCreatures"), Loc.T("Off")]);

        CategoryChips.Children.Clear();
        AddChip(Loc.T("CatAll"), null);
        foreach (var c in SkillData.Categories) AddChip(SkillData.LabelOf(c), c);
    }

    static void SetItems(ComboBox box, string[] items)
    {
        int idx = box.SelectedIndex;
        box.ItemsSource = items;
        box.SelectedIndex = idx < 0 ? 0 : Math.Min(idx, items.Length - 1);
    }

    void AddChip(string text, SkillCategory? cat)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (cat is SkillCategory c)
            content.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = SkillData.ColorOf(c).Brush(), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = text });
        var rb = new RadioButton { Content = content, GroupName = "cat", IsChecked = chipFilter == cat };
        rb.Classes.Add("chip");
        rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true) { chipFilter = cat; RefreshList(); } };
        CategoryChips.Children.Add(rb);
    }

    // ===================================================================== stats (factory tab)

    void StatBox_Changed(TextBox tb)
    {
        if (!ready || loading || tb.Tag is not string key) return;
        string text = (tb.Text ?? "").Replace(".", "").Replace(",", "").Replace(" ", "").Replace("'", "");
        long max = key == "money" ? MoneyMax : int.MaxValue;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) && v >= 0 && v <= max)
        {
            SetBad(tb, false);
            EnsureSave().SetPath(key, v);
            MarkDirty();
        }
        else SetBad(tb, true);
    }

    UnityJson EnsureSave() => save ??= NewSave();

    void Quick_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string tag }) return;
        var parts = tag.Split(':');
        string key = parts[0];
        var s = EnsureSave();
        long cur = s.GetPath(key);
        long max = key == "money" ? MoneyMax : int.MaxValue;
        long v = parts[1] switch
        {
            "max" => max,
            "zero" => 0,
            "one" => 1,
            var n => cur + long.Parse(n, CultureInfo.InvariantCulture),
        };
        v = Math.Clamp(v, key == "day" ? 1 : 0, max);
        SetStat(key, v);
    }

    void SetStat(string key, long v)
    {
        EnsureSave().SetPath(key, v);
        loading = true;
        foreach (var box in NumberBoxes.Where(b => (string)b.Tag! == key))
        {
            box.Text = v.ToString(CultureInfo.InvariantCulture);
            SetBad(box, false);
        }
        loading = false;
        MarkDirty();
    }

    void Options_Click(object? sender, RoutedEventArgs e)
    {
        appSettings.AutoRequirements = AutoReqSwitch.IsChecked == true;
        appSettings.AllowDemoLocked = AllowDemoSwitch.IsChecked == true;
        appSettings.Save();
        RefreshSkills();
    }

    bool AutoReq => AutoReqSwitch.IsChecked == true;
    bool AllowDemo => AllowDemoSwitch.IsChecked == true;
    bool Permitted(SkillDef d) => AllowDemo || !d.DemoLocked;

    // ===================================================================== skills

    void ToggleSkill(int id) => SetOwned([id], !ownedSet.Contains(id));

    /// <summary>Unlock or lock skills; with auto requirements the tree stays consistent.</summary>
    void SetOwned(IEnumerable<int> ids, bool on)
    {
        var list = ids.ToList();
        int blocked = 0;
        if (on)
        {
            var add = new List<int>();
            foreach (int id in list)
            {
                if (ownedSet.Contains(id)) continue;
                List<int> chain = AutoReq ? SkillData.WithRequirements(id) : [id];
                // a chain through a demo-locked skill is skipped as a whole, so the tree never gets holes
                if (chain.Any(x => !ownedSet.Contains(x) && !Permitted(SkillData.ById[x]))) { blocked++; continue; }
                foreach (int x in chain)
                    if (!ownedSet.Contains(x) && !add.Contains(x)) add.Add(x);
            }
            foreach (int x in SkillData.TopologicalOrder(add))
                if (ownedSet.Add(x)) owned.Add(x);
        }
        else
        {
            var remove = new HashSet<int>();
            foreach (int id in list)
                foreach (int x in AutoReq ? SkillData.WithDependents(id) : [id]) remove.Add(x);
            owned.RemoveAll(remove.Contains);
            ownedSet.ExceptWith(remove);
        }
        EnsureSave().SetIntList("unlockedSkills", owned);
        RefreshSkills();
        MarkDirty();
        if (blocked > 0) SetStatus(Loc.T("DemoLocked") + ": " + blocked + " ✕  (" + Loc.T("AllowDemo") + ")", StatusKind.Warn);
    }

    void RefreshSkills()
    {
        tree.RefreshAll();
        foreach (var vm in skillVms) vm.Refresh();
        foreach (var c in categoryVms)
        {
            c.OwnedCount = SkillData.All.Count(s => s.Category == c.Category && ownedSet.Contains(s.Id));
            c.Refresh();
        }
        int total = SkillData.All.Count;
        ProgressText.Text = Loc.T("SkillProgressText", owned.Count, total);
        double pct = 100.0 * owned.Count / total;
        ProgressPercent.Text = pct.ToString("0", Loc.Instance.Culture) + " %";
        ProgressBarAll.Value = pct;
        long spent = SkillData.All.Where(s => ownedSet.Contains(s.Id)).Sum(s => s.Cost);
        long rest = SkillData.All.Where(s => !ownedSet.Contains(s.Id) && Permitted(s)).Sum(s => s.Cost);
        SpentText.Text = "$ " + Loc.Short(spent);
        RemainingText.Text = "$ " + Loc.Short(rest);
        int demoOwned = SkillData.All.Count(s => s.DemoLocked && ownedSet.Contains(s.Id));
        DemoText.Text = $"{demoOwned} / {SkillData.All.Count(s => s.DemoLocked)}";
        if (tree.SelectedId is int id && DetailPanel.IsVisible) ShowDetail(id);
        RefreshList();
        RefreshHeader();
    }

    void UnlockAll_Click(object? sender, RoutedEventArgs e) => SetOwned(SkillData.All.Select(s => s.Id), true);

    void Everything_Click(object? sender, RoutedEventArgs e)
    {
        SetOwned(SkillData.All.Select(s => s.Id), true);
        SetPermanent(GameData.Permanent.Select(p => p.Id), true);
    }
    void LockAll_Click(object? sender, RoutedEventArgs e) => SetOwned(SkillData.All.Select(s => s.Id), false);

    /// <summary>Early-game weapons only (bat, knife, pistol, SMG) plus the vacuum, with their requirements.</summary>
    static readonly string[] StarterKeys = ["Vacuum", "Bat", "Knife", "Pistol", "Uzi"];

    void StarterPack_Click(object? sender, RoutedEventArgs e) =>
        SetOwned(SkillData.All.Where(s => StarterKeys.Contains(s.Key)).Select(s => s.Id), true);

    void MoneyForRest_Click(object? sender, RoutedEventArgs e)
    {
        long rest = SkillData.All.Where(s => !ownedSet.Contains(s.Id) && Permitted(s)).Sum(s => s.Cost);
        long cur = EnsureSave().GetLong("money");
        SetStat("money", Math.Min(MoneyMax, cur + rest));
    }

    void CategoryOn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: SkillCategory c })
            SetOwned(SkillData.All.Where(s => s.Category == c).Select(s => s.Id), true);
    }

    void CategoryOff_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: SkillCategory c })
            SetOwned(SkillData.All.Where(s => s.Category == c).Select(s => s.Id), false);
    }

    // ---- tree tab

    void ZoomIn_Click(object? sender, RoutedEventArgs e) => tree.Zoom(1.25);
    void ZoomOut_Click(object? sender, RoutedEventArgs e) => tree.Zoom(1 / 1.25);
    void ZoomFit_Click(object? sender, RoutedEventArgs e) => tree.Fit();

    void ShowDetail(int id)
    {
        if (!SkillData.ById.TryGetValue(id, out var d)) return;
        var vm = skillVms.First(v => v.Id == id);
        DetailEmpty.IsVisible = false;
        DetailPanel.IsVisible = true;
        var color = SkillData.ColorOf(d.Category);
        bool own = ownedSet.Contains(id);
        DetailDisc.Stroke = own ? new ImmutableSolidColorBrush(Color.FromRgb(0xF5, 0xE6, 0xCC)) : color.Brush();
        DetailDisc.Fill = own ? color.Brush() : Res("Panel2");
        DetailIcon.Source = Ui.Icon(d.Icon);
        DetailName.Text = d.Name;
        DetailMeta.Text = vm.Meta;
        DetailMeta.Foreground = color.Brush();
        DetailDesc.Text = d.Description;
        DetailPrice.Text = "$ " + Loc.Num(d.Cost);
        DetailDemo.IsVisible = d.DemoLocked;
        DetailToggle.Content = own ? Loc.T("Lock") : Loc.T("Unlock");
        DetailToggle.Tag = id;

        var reqs = d.Requirements.Select(r => skillVms.First(v => v.Id == r)).ToList();
        var next = d.Unlocks.Select(u => skillVms.First(v => v.Id == u.Id)).ToList();
        DetailRequires.ItemsSource = reqs;
        DetailUnlocks.ItemsSource = next;
        DetailRequiresNone.IsVisible = reqs.Count == 0;
        DetailUnlocksNone.IsVisible = next.Count == 0;
    }

    void DetailToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (DetailToggle.Tag is int id) { ToggleSkill(id); ShowDetail(id); }
    }

    void DetailBranch_Click(object? sender, RoutedEventArgs e)
    {
        if (DetailToggle.Tag is not int id) return;
        SetOwned(SkillData.WithDependents(id).Concat(SkillData.WithRequirements(id)), true);
        ShowDetail(id);
    }

    void LinkRow_Click(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: SkillVM vm })
        {
            tree.Select(vm.Id, center: true);
            ShowDetail(vm.Id);
        }
    }

    // ---- list tab

    bool FilterSkill(SkillVM vm)
    {
        if (chipFilter is SkillCategory c && vm.Def.Category != c) return false;
        switch (StatusFilter.SelectedIndex)
        {
            case 1 when !vm.Owned: return false;
            case 2 when vm.Owned: return false;
            case 3 when !vm.DemoLocked: return false;
        }
        string q = (SearchBox.Text ?? "").Trim();
        if (q.Length == 0) return true;
        return vm.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
               || vm.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
               || vm.Def.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
               || vm.Id.ToString(CultureInfo.InvariantCulture) == q;
    }

    void StatusFilter_Changed(object? sender, SelectionChangedEventArgs e) { if (ready) RefreshList(); }

    void RefreshList()
    {
        int shown = 0;
        foreach (var vm in skillVms)
        {
            bool show = FilterSkill(vm);
            if (show) shown++;
            if (vm.Shown != show) { vm.Shown = show; vm.Refresh(); }
        }
        ShownText.Text = Loc.T("ShownN", shown);
    }

    IEnumerable<int> FilteredIds() => skillVms.Where(FilterSkill).Select(v => v.Id).ToList();

    void UnlockFiltered_Click(object? sender, RoutedEventArgs e) => SetOwned(FilteredIds(), true);
    void LockFiltered_Click(object? sender, RoutedEventArgs e) => SetOwned(FilteredIds(), false);

    void SkillCard_Click(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: SkillVM vm }) ToggleSkill(vm.Id);
    }

    // ===================================================================== prestige (full game)

    void SetPermanent(IEnumerable<int> ids, bool on)
    {
        foreach (int id in ids)
        {
            if (on && permOwned.Add(id)) permOrder.Add(id);
            else if (!on && permOwned.Remove(id)) permOrder.Remove(id);
        }
        EnsureSave().SetIntList("unlockedPermanentUpgrades", permOrder);
        RefreshPermanent();
        MarkDirty();
    }

    void RefreshPermanent()
    {
        foreach (var vm in permVms) vm.Refresh();
        int total = GameData.Permanent.Count, count = GameData.Permanent.Count(p => permOwned.Contains(p.Id));
        long spent = GameData.Permanent.Where(p => permOwned.Contains(p.Id)).Sum(p => (long)p.Cost);
        long all = GameData.Permanent.Sum(p => (long)p.Cost);
        PermProgressText.Text = Loc.T("PermProgress", count, total, Loc.Num(spent), Loc.Num(all));
        PermProgressBar.Value = total == 0 ? 0 : 100.0 * count / total;
    }

    void PermCard_Click(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: PermanentVM vm }) SetPermanent([vm.Id], !permOwned.Contains(vm.Id));
    }

    void PermAllOn_Click(object? sender, RoutedEventArgs e) => SetPermanent(GameData.Permanent.Select(p => p.Id), true);
    void PermAllOff_Click(object? sender, RoutedEventArgs e) => SetPermanent(GameData.Permanent.Select(p => p.Id), false);

    void PermPoints_Click(object? sender, RoutedEventArgs e)
    {
        long rest = GameData.Permanent.Where(p => !permOwned.Contains(p.Id)).Sum(p => (long)p.Cost);
        SetStat("upgradePoints", Math.Min(int.MaxValue, EnsureSave().GetLong("upgradePoints") + rest));
    }

    void EndingSwitch_Click(object? sender, RoutedEventArgs e)
    {
        EnsureSave().SetBool("hasSeenEnding", EndingSwitch.IsChecked == true);
        MarkDirty();
    }

    // ===================================================================== mails (full game)

    void LoadMailStates()
    {
        foreach (var vm in mailVms) { vm.Received = vm.Opened = vm.Accepted = false; vm.ReceivedDay = 0; }
        if (save?.Root["mails"] is not JsonArray arr) return;
        foreach (var n in arr)
        {
            var vm = mailVms.FirstOrDefault(m => m.Id == UnityJson.Long(n, "id", -1));
            if (vm is null) continue;
            vm.Received = true;
            vm.ReceivedDay = UnityJson.Long(n, "receivedDay");
            vm.Opened = UnityJson.Bool(n, "opened");
            vm.Accepted = UnityJson.Bool(n, "accepted");
        }
    }

    /// <summary>Writes one mail's state into the "mails" array (keeps order and unknown fields of existing entries).</summary>
    void SyncMail(MailVM vm)
    {
        var arr = EnsureSave().Array("mails");
        var entry = arr.OfType<JsonObject>().FirstOrDefault(o => UnityJson.Long(o, "id", -1) == vm.Id);
        if (!vm.Received)
        {
            if (entry is not null) arr.Remove(entry);
            return;
        }
        if (entry is null)
        {
            entry = new JsonObject { ["id"] = (long)vm.Id };
            arr.Add(entry);
        }
        entry["receivedDay"] = vm.ReceivedDay;
        entry["opened"] = vm.Opened;
        entry["accepted"] = vm.Accepted;
    }

    void ChangeMails(IEnumerable<MailVM> vms, Action<MailVM> change)
    {
        foreach (var vm in vms.ToList())
        {
            change(vm);
            if (!vm.Received) vm.Opened = vm.Accepted = false;
            SyncMail(vm);
        }
        RefreshMails();
        MarkDirty();
        if (MailList.SelectedItem is MailVM sel) ShowMail(sel);
    }

    // receivedDay keeps counting across factory sales (run day resets, mail days don't),
    // so new mails go on the latest day already in the inbox when that is higher.
    long DefaultMailDay => Math.Max(Math.Max(0, (save ?? NewSave()).GetLong("day", 1) - 1),
                                    mailVms.Where(m => m.Received).Select(m => m.ReceivedDay).DefaultIfEmpty(0).Max());

    void RefreshMails()
    {
        foreach (var vm in mailVms) vm.Refresh();
        var selected = MailList.SelectedItem;
        var shown = mailVms.Where(FilterMail).ToList();
        if (MailList.ItemsSource is not List<MailVM> cur || !cur.SequenceEqual(shown))
        {
            MailList.ItemsSource = shown;
            if (selected is MailVM s && shown.Contains(s)) MailList.SelectedItem = s;
        }
        MailsCountText.Text = Loc.T("MailsCount", mailVms.Count(m => m.Received), mailVms.Count, mailVms.Count(m => m.Received && !m.Opened));
    }

    bool FilterMail(MailVM vm) => MailFilter.SelectedIndex switch
    {
        1 => vm.Received,
        2 => !vm.Received,
        3 => vm.Received && !vm.Opened,
        _ => true,
    };

    void MailFilter_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (ready && mailVms is not null) RefreshMails();
    }

    void MailList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ready && MailList.SelectedItem is MailVM vm) ShowMail(vm);
    }

    void ShowMail(MailVM vm)
    {
        bool was = loading;
        loading = true;
        MailEmpty.IsVisible = false;
        MailPanel.IsVisible = true;
        MailSender.Text = vm.SenderName;
        MailSender.Foreground = vm.SenderBrush;
        MailTitle.Text = vm.Title;
        MailMeta.Text = $"#{vm.Id} · {vm.Def.Key} · {vm.StateText}";
        MailReceivedSwitch.IsChecked = vm.Received;
        MailOpenedSwitch.IsChecked = vm.Opened;
        MailAcceptedSwitch.IsChecked = vm.Accepted;
        MailAcceptedSwitch.IsVisible = MailAcceptBanner.IsVisible = vm.Def.Acceptable;
        MailDayBox.Text = vm.ReceivedDay.ToString(CultureInfo.InvariantCulture);
        MailDayBox.IsEnabled = vm.Received;
        SetBad(MailDayBox, false);
        MailBody.Text = vm.Def.Body;
        loading = was;
    }

    void MailState_Click(object? sender, RoutedEventArgs e)
    {
        if (MailList.SelectedItem is not MailVM vm) return;
        bool received = MailReceivedSwitch.IsChecked == true, opened = MailOpenedSwitch.IsChecked == true, accepted = MailAcceptedSwitch.IsChecked == true;
        if (sender != MailReceivedSwitch && (opened || accepted)) received = true;
        ChangeMails([vm], m =>
        {
            if (received && !m.Received) m.ReceivedDay = DefaultMailDay;
            m.Received = received;
            m.Opened = opened;
            m.Accepted = accepted;
        });
    }

    void MailDay_Changed()
    {
        if (!ready || loading || MailList.SelectedItem is not MailVM vm || !vm.Received) return;
        if (long.TryParse(MailDayBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long d) && d >= 0 && d < 100000)
        {
            SetBad(MailDayBox, false);
            vm.ReceivedDay = d;
            SyncMail(vm);
            vm.Refresh();
            MarkDirty();
        }
        else SetBad(MailDayBox, true);
    }

    void MailReceiveAll_Click(object? sender, RoutedEventArgs e) =>
        ChangeMails(mailVms.Where(m => !m.Received), m => { m.Received = true; m.ReceivedDay = DefaultMailDay; });

    void MailReadAll_Click(object? sender, RoutedEventArgs e) =>
        ChangeMails(mailVms.Where(m => m.Received), m => m.Opened = true);

    void MailRemoveAll_Click(object? sender, RoutedEventArgs e) =>
        ChangeMails(mailVms.Where(m => m.Received), m => m.Received = false);

    // ===================================================================== game settings

    void PopulateSettings()
    {
        bool was = loading;
        loading = true;
        GameLangCombo.SelectedIndex = (int)Math.Clamp(settings.GetLong("languageIndex"), 0, 1);
        SensSlider.Value = settings.GetDouble("mouseSensitivity", 1.0);
        MasterSlider.Value = settings.GetDouble("masterVolume", 100);
        MusicSlider.Value = settings.GetDouble("musicVolume", 100);
        SfxSlider.Value = settings.GetDouble("sfxVolume", 100);
        WindowModeCombo.SelectedIndex = (int)Math.Clamp(settings.GetLong("windowModeIndex", 1), 0, 3);
        ResolutionBox.Text = settings.GetLong("resolutionIndex").ToString(CultureInfo.InvariantCulture);
        AACombo.SelectedIndex = (int)Math.Clamp(settings.GetLong("antialiasingIndex"), 0, 2);
        ReflCombo.SelectedIndex = (int)Math.Clamp(settings.GetLong("reflectionsIndex", 1), 0, 2);
        VSyncSwitch.IsChecked = settings.GetBool("vSync", true);
        ShadowsSwitch.IsChecked = settings.GetBool("shadows", true);
        AOSwitch.IsChecked = settings.GetBool("ambientOcclusion", true);
        BloomSwitch.IsChecked = settings.GetBool("bloom", true);
        CASwitch.IsChecked = settings.GetBool("chromaticAberration", true);
        FovSlider.Value = Math.Clamp(settings.GetLong("fieldOfView", 60), 40, 90);
        InvertYSwitch.IsChecked = settings.GetBool("invertY");
        HeadBobSwitch.IsChecked = settings.GetBool("headBob", true);
        BloodCombo.SelectedIndex = (int)Math.Clamp(settings.GetLong("bloodIndex"), 0, 2);
        WoundsCombo.SelectedIndex = (int)Math.Clamp(settings.GetLong("woundsIndex"), 0, 2);
        NoSettingsBanner.IsVisible = !settingsExists;
        UpdateSliderTexts();
        loading = was;
    }

    void UpdateSliderTexts()
    {
        SensText.Text = SensSlider.Value.ToString("0.00", Loc.Instance.Culture);
        MasterText.Text = MasterSlider.Value.ToString("0") + " %";
        MusicText.Text = MusicSlider.Value.ToString("0") + " %";
        SfxText.Text = SfxSlider.Value.ToString("0") + " %";
        FovText.Text = FovSlider.Value.ToString("0") + "°";
    }

    void Slider_Changed(object? sender, RangeBaseValueChangedEventArgs e) => Setting_Changed(sender, e);

    void Setting_Changed(object? sender, RoutedEventArgs e)
    {
        if (!ready) return; // events fired while InitializeComponent builds the controls
        UpdateSliderTexts();
        if (loading) return;
        switch (sender)
        {
            case var _ when sender == GameLangCombo && GameLangCombo.SelectedIndex >= 0: settings.SetLong("languageIndex", GameLangCombo.SelectedIndex); break;
            case var _ when sender == SensSlider: settings.SetDouble("mouseSensitivity", Math.Round(SensSlider.Value, 2)); break;
            case var _ when sender == MasterSlider: settings.SetDouble("masterVolume", Math.Round(MasterSlider.Value)); break;
            case var _ when sender == MusicSlider: settings.SetDouble("musicVolume", Math.Round(MusicSlider.Value)); break;
            case var _ when sender == SfxSlider: settings.SetDouble("sfxVolume", Math.Round(SfxSlider.Value)); break;
            case var _ when sender == WindowModeCombo && WindowModeCombo.SelectedIndex >= 0: settings.SetLong("windowModeIndex", WindowModeCombo.SelectedIndex); break;
            case var _ when sender == AACombo && AACombo.SelectedIndex >= 0: settings.SetLong("antialiasingIndex", AACombo.SelectedIndex); break;
            case var _ when sender == ReflCombo && ReflCombo.SelectedIndex >= 0: settings.SetLong("reflectionsIndex", ReflCombo.SelectedIndex); break;
            case var _ when sender == VSyncSwitch: settings.SetBool("vSync", VSyncSwitch.IsChecked == true); break;
            case var _ when sender == ShadowsSwitch: settings.SetBool("shadows", ShadowsSwitch.IsChecked == true); break;
            case var _ when sender == AOSwitch: settings.SetBool("ambientOcclusion", AOSwitch.IsChecked == true); break;
            case var _ when sender == BloomSwitch: settings.SetBool("bloom", BloomSwitch.IsChecked == true); break;
            case var _ when sender == CASwitch: settings.SetBool("chromaticAberration", CASwitch.IsChecked == true); break;
            case var _ when sender == FovSlider: settings.SetLong("fieldOfView", (long)Math.Round(FovSlider.Value)); break;
            case var _ when sender == InvertYSwitch: settings.SetBool("invertY", InvertYSwitch.IsChecked == true); break;
            case var _ when sender == HeadBobSwitch: settings.SetBool("headBob", HeadBobSwitch.IsChecked == true); break;
            case var _ when sender == BloodCombo && BloodCombo.SelectedIndex >= 0: settings.SetLong("bloodIndex", BloodCombo.SelectedIndex); break;
            case var _ when sender == WoundsCombo && WoundsCombo.SelectedIndex >= 0: settings.SetLong("woundsIndex", WoundsCombo.SelectedIndex); break;
            case var _ when sender == ResolutionBox:
                if (int.TryParse(ResolutionBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) && r >= 0 && r < 1000)
                {
                    SetBad(ResolutionBox, false);
                    settings.SetLong("resolutionIndex", r);
                }
                else
                {
                    SetBad(ResolutionBox, true);
                    return;
                }
                break;
            default: return;
        }
        MarkDirty();
    }

    void ApplyPreset(long aa, bool shadows, long refl, bool ao, bool bloom, bool ca, bool vsync)
    {
        settings.SetLong("antialiasingIndex", aa);
        settings.SetBool("shadows", shadows);
        settings.SetLong("reflectionsIndex", refl);
        settings.SetBool("ambientOcclusion", ao);
        settings.SetBool("bloom", bloom);
        settings.SetBool("chromaticAberration", ca);
        settings.SetBool("vSync", vsync);
        PopulateSettings();
        MarkDirty();
    }

    void PresetUltra_Click(object? sender, RoutedEventArgs e) => ApplyPreset(0, true, 0, true, true, true, true);
    void PresetPerformance_Click(object? sender, RoutedEventArgs e) => ApplyPreset(2, false, 2, false, false, false, false);

    void PresetDefaults_Click(object? sender, RoutedEventArgs e)
    {
        var d = DefaultSettings();
        foreach (var key in new[] { "mouseSensitivity", "masterVolume", "musicVolume", "sfxVolume" }) settings.SetDouble(key, d.GetDouble(key));
        foreach (var key in new[] { "windowModeIndex", "antialiasingIndex", "reflectionsIndex" }) settings.SetLong(key, d.GetLong(key));
        foreach (var key in new[] { "vSync", "shadows", "ambientOcclusion", "bloom", "chromaticAberration" }) settings.SetBool(key, d.GetBool(key));
        if (IsFull)
        {
            foreach (var key in new[] { "fieldOfView", "bloodIndex", "woundsIndex" }) settings.SetLong(key, d.GetLong(key));
            foreach (var key in new[] { "invertY", "headBob" }) settings.SetBool(key, d.GetBool(key));
        }
        PopulateSettings();
        MarkDirty();
    }

    // ===================================================================== save / discard

    async void Save_Click(object? sender, RoutedEventArgs e) => await SaveAll();

    async Task<bool> SaveAll()
    {
        if (folder is null) return false;
        if (SaveStore.IsGameRunning())
        {
            UpdateRunningState();
            await Dialog.Show(this, Loc.T("SaveBlockedRunning"), DialogButtons.Ok, warn: true);
            return false;
        }

        bool changedOnDisk =
            (save is not null && folder.HasSave && File.GetLastWriteTimeUtc(folder.SavePath) != saveStamp && savedSaveText.Length > 0) ||
            (settingsExists && folder.HasSettings && File.GetLastWriteTimeUtc(folder.SettingsPath) != settingsStamp);
        if (changedOnDisk && await Dialog.Show(this, Loc.T("ChangedOnDisk"), DialogButtons.YesNo, warn: true) != DialogResult.Yes)
            return false;

        try
        {
            string newSave = save?.ToString() ?? "";
            string newSettings = settings.ToString();
            bool writeSave = save is not null && newSave != savedSaveText;
            bool writeSettings = newSettings != savedSettingsText;

            string? backup = (writeSave || writeSettings) ? SaveStore.CreateBackup(folder) : null;
            if (writeSave)
            {
                UnityJson.Parse(newSave); // sanity: never write what we cannot read back
                SaveStore.WriteAtomic(folder.SavePath, newSave);
                savedSaveText = newSave;
                saveStamp = File.GetLastWriteTimeUtc(folder.SavePath);
            }
            if (writeSettings)
            {
                UnityJson.Parse(newSettings);
                SaveStore.WriteAtomic(folder.SettingsPath, newSettings);
                savedSettingsText = newSettings;
                settingsExists = true;
                settingsStamp = File.GetLastWriteTimeUtc(folder.SettingsPath);
                NoSettingsBanner.IsVisible = false;
            }
            SetDirty(false);
            NoSaveBanner.IsVisible = save is null;
            SetStatus(backup is null ? Loc.T("SavedNoBackup") : Loc.T("Saved", Path.GetFileName(backup)), StatusKind.Good);
            ScanFolders(folder.Path);
            RefreshBackups();
            RefreshHeader();
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("SaveFailed", ex.Message), StatusKind.Bad);
            await Dialog.Show(this, Loc.T("SaveFailed", ex.Message), DialogButtons.Ok, warn: true);
            return false;
        }
    }

    async void Discard_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard()) return;
        if (folder is not null) LoadFolder(folder);
    }

    async Task<bool> ConfirmDiscard() =>
        !dirty || await Dialog.Show(this, Loc.T("DiscardConfirm"), DialogButtons.YesNo) == DialogResult.Yes;

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!dirty || closeConfirmed || App.SnapshotDir is not null || App.SelfTestLog is not null) return;
        e.Cancel = true; // the question is async: ask first, then close again
        var r = await Dialog.Show(this, Loc.T("SaveQuestion"), DialogButtons.YesNoCancel);
        if (r == DialogResult.Cancel || (r == DialogResult.Yes && !await SaveAll())) return;
        closeConfirmed = true;
        Close();
    }

    async void Reload_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard()) return;
        var path = folder?.Path;
        folder = null;
        ScanFolders(path);
        if (folder is null && FolderList.SelectedItem is FolderItem item) LoadFolder(item.Folder);
    }

    /// <summary>Opens a folder or URL with the desktop's default handler (xdg-open on Linux).</summary>
    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception) { /* no handler installed */ }
    }

    void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (folder is null) return;
        Directory.CreateDirectory(folder.Path);
        Open(folder.Path);
    }

    void LaunchGame_Click(object? sender, RoutedEventArgs e) => Open($"steam://rungameid/{Editions.AppId(edition)}");

    // ===================================================================== backups

    void RefreshBackups()
    {
        if (folder is null) return;
        var items = SaveStore.ListBackups(folder).Select(b => new BackupItem(b,
            b.Time.ToString("F", Loc.Instance.Culture),
            b.HasSave ? Loc.T("BackupLine", Loc.Num(b.Money), b.Day, b.Skills) + (b.HasSettings ? "  ·  ⚙" : "") : "⚙ settings.sav")).ToList();
        BackupList.ItemsSource = items;
        NoBackupsText.IsVisible = items.Count == 0;
    }

    void CreateBackup_Click(object? sender, RoutedEventArgs e)
    {
        if (folder is null) return;
        var dest = SaveStore.CreateBackup(folder, "_manual");
        if (dest is not null) SetStatus(Loc.T("BackupCreated", Path.GetFileName(dest)), StatusKind.Good);
        RefreshBackups();
    }

    async void RestoreBackup_Click(object? sender, RoutedEventArgs e)
    {
        if (folder is null || BackupList.SelectedItem is not BackupItem item) return;
        if (SaveStore.IsGameRunning())
        {
            await Dialog.Show(this, Loc.T("SaveBlockedRunning"), DialogButtons.Ok, warn: true);
            return;
        }
        if (await Dialog.Show(this, Loc.T("RestoreConfirm", item.Entry.Name), DialogButtons.YesNo) != DialogResult.Yes)
            return;
        SaveStore.CreateBackup(folder, "_before-restore");
        SaveStore.RestoreBackup(folder, item.Entry);
        var f = folder;
        dirty = false;
        LoadFolder(f);
        ScanFolders(f.Path);
        SetStatus(Loc.T("Restored", item.Entry.Name), StatusKind.Good);
    }

    async void DeleteBackup_Click(object? sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not BackupItem item) return;
        if (await Dialog.Show(this, Loc.T("DeleteConfirm", item.Entry.Name), DialogButtons.YesNo, warn: true) != DialogResult.Yes)
            return;
        Directory.Delete(item.Entry.Folder, true);
        RefreshBackups();
    }

    void OpenBackupFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (folder is null) return;
        Directory.CreateDirectory(folder.BackupRoot);
        Open(folder.BackupRoot);
    }

    // ===================================================================== raw json

    void Tabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ready || e.Source != Tabs) return;
        if (Tabs.SelectedItem == RawTab) RawRefresh();
    }

    void RawRefresh()
    {
        RawSave.Text = save?.ToString() ?? "";
        RawSettings.Text = settings.ToString();
    }

    void RawRefresh_Click(object? sender, RoutedEventArgs e) => RawRefresh();

    void RawApply_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var newSave = string.IsNullOrWhiteSpace(RawSave.Text) ? null : UnityJson.Parse(RawSave.Text);
            var newSettings = UnityJson.Parse(RawSettings.Text ?? "", SaveStore.SettingsFloatKeys);
            save = newSave;
            settings = newSettings;
            PopulateAll();
            SetDirty(true);
            SetStatus(Loc.T("RawApplied"), StatusKind.Good);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("RawInvalid", ex.Message), StatusKind.Bad);
        }
    }

    static readonly FilePickerFileType SaveFiles = new("Save (*.sav, *.json)") { Patterns = ["*.sav", "*.json"] };

    async void Export_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "save.sav",
            FileTypeChoices = [SaveFiles, FilePickerFileTypes.All],
        });
        if (file?.TryGetLocalPath() is not string path) return;
        File.WriteAllText(path, save?.ToString() ?? RawSave.Text ?? "", new System.Text.UTF8Encoding(false));
    }

    async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [SaveFiles, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not string path) return;
        RawSave.Text = File.ReadAllText(path);
        RawApply_Click(sender, e);
    }

    // ===================================================================== snapshot (UI test)

    async Task RunSnapshots(string dir)
    {
        Directory.CreateDirectory(dir);
        WindowState = WindowState.Maximized;
        await Task.Delay(800);
        var client = (Control)Content!;
        double scaling = RenderScaling;
        for (int i = 0; i < Tabs.ItemCount; i++)
        {
            if (Tabs.Items[i] is TabItem { IsVisible: false }) continue;
            Tabs.SelectedIndex = i;
            if (i == 1) { await Task.Delay(300); tree.Fit(); ShowDetail(183); tree.Select(183); }
            if (Tabs.SelectedItem == MailsTab && mailVms.Count > 0) MailList.SelectedItem = mailVms.FirstOrDefault(m => m.Received) ?? mailVms[0];
            await Task.Delay(700);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var size = new PixelSize((int)Math.Round(client.Bounds.Width * scaling), (int)Math.Round(client.Bounds.Height * scaling));
            using var bmp = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
            bmp.Render(client);
            bmp.Save(Path.Combine(dir, $"tab{i}.png"));
        }
        Close();
    }

    async Task RunSelfTest(string log)
    {
        if (IsFull) { await RunSelfTestFull(log); return; }
        var lines = new List<string> { "linux front end, edition " + edition + ", skills " + SkillData.All.Count };
        void Check(string name, bool ok) => lines.Add((ok ? "PASS " : "FAIL ") + name);
        try
        {
            Check("roundtrip save byte-exact", save is null || save.ToString() == savedSaveText);
            Check("roundtrip settings byte-exact", !settingsExists || settings.ToString() == savedSettingsText);

            AutoReqSwitch.IsChecked = true;
            AllowDemoSwitch.IsChecked = false;
            SetOwned([230], true);
            Check("rocket blocked (chain via demo-locked AK-47)", !ownedSet.Contains(230) && !ownedSet.Contains(225));
            SetOwned([183], true);
            Check("vacuum unlocked with requirements", ownedSet.Contains(183) && ownedSet.Contains(60) && ownedSet.Contains(53));
            AllowDemoSwitch.IsChecked = true;
            SetOwned([230], true);
            AllowDemoSwitch.IsChecked = false;
            var order = owned.ToList();
            bool topo = order.All(id => SkillData.ById[id].Requirements.All(r => order.IndexOf(r) >= 0 && order.IndexOf(r) < order.IndexOf(id)));
            Check("rocket unlocked with requirements in order", ownedSet.Contains(230) && ownedSet.Contains(225) && topo);

            SetOwned([225], false);
            Check("locking shotgun removes rocket", !ownedSet.Contains(225) && !ownedSet.Contains(230) && ownedSet.Contains(227));

            SetOwned([227], false);
            SetOwned([227], true);
            Check("demo-locked AK-47 blocked", !ownedSet.Contains(227));

            AllowDemoSwitch.IsChecked = true;
            UnlockAll_Click(this, new RoutedEventArgs());
            Check("unlock all = 231", owned.Count == SkillData.All.Count);
            order = owned.ToList();
            Check("unlock all keeps valid purchase order", order.All(id => SkillData.ById[id].Requirements.All(r => order.IndexOf(r) < order.IndexOf(id))));

            SetStat("money", 123456789);
            MoneyBox.Text = "2147483648"; // over max -> rejected
            Check("money over max rejected", EnsureSave().GetLong("money") == 123456789);
            SensSlider.Value = 2.5;
            MasterSlider.Value = 40;
            AACombo.SelectedIndex = 1;
            CASwitch.IsChecked = false; Setting_Changed(CASwitch, new RoutedEventArgs());

            bool saved = await SaveAll();
            Check("save ok", saved);
            var s2 = UnityJson.Parse(File.ReadAllText(folder!.SavePath));
            Check("money written", s2.GetLong("money") == 123456789);
            Check("skills written", s2.GetIntList("unlockedSkills").Count == 231);
            var st = UnityJson.Parse(File.ReadAllText(folder.SettingsPath));
            Check("sens written", Math.Abs(st.GetDouble("mouseSensitivity") - 2.5) < 1e-9);
            Check("master written as float", File.ReadAllText(folder.SettingsPath).Contains("\"masterVolume\": 40.0"));
            Check("aa written", st.GetLong("antialiasingIndex") == 1);
            Check("ca written", !st.GetBool("chromaticAberration", true));
            Check("backup created", SaveStore.ListBackups(folder).Count >= 1);
            Check("no BOM", File.ReadAllBytes(folder.SavePath)[0] == (byte)'{');
        }
        catch (Exception ex) { lines.Add("FAIL exception " + ex); }
        File.WriteAllLines(log, lines);
        Close();
    }

    async Task RunSelfTestFull(string log)
    {
        var lines = new List<string> { "linux front end, edition " + edition + ", skills " + SkillData.All.Count };
        void Check(string name, bool ok) => lines.Add((ok ? "PASS " : "FAIL ") + name);
        try
        {
            Check("roundtrip save byte-exact", save is null || save.ToString() == savedSaveText);
            Check("roundtrip settings byte-exact", !settingsExists || settings.ToString() == savedSettingsText);
            Check("252 skills, robots + turrets categories", SkillData.All.Count == 252 && SkillData.Categories.Contains(SkillCategory.Robots) && SkillData.Categories.Contains(SkillCategory.Turrets));
            Check("no demo locks in full game", SkillData.All.All(d => !d.DemoLocked));

            AutoReqSwitch.IsChecked = true;
            LockAll_Click(this, new RoutedEventArgs());
            StarterPack_Click(this, new RoutedEventArgs());
            Check("starter pack = vacuum + bat..SMG, nothing after", new[] { 183, 223, 224, 221, 222 }.All(ownedSet.Contains)
                  && !new[] { 227, 253, 225, 230, 228 }.Any(ownedSet.Contains));

            SetOwned([257], true);
            var order = owned.ToList();
            bool topo = order.All(id => SkillData.ById[id].Requirements.All(r => order.IndexOf(r) >= 0 && order.IndexOf(r) < order.IndexOf(id)));
            Check("robot 2 unlocked with requirements in order", ownedSet.Contains(257) && ownedSet.Contains(255) && ownedSet.Contains(261) && ownedSet.Contains(254) && topo);
            SetOwned([254], false);
            Check("locking robot 1 removes robot 2", !ownedSet.Contains(254) && !ownedSet.Contains(257) && !ownedSet.Contains(255));

            UnlockAll_Click(this, new RoutedEventArgs());
            order = owned.ToList();
            Check("unlock all = 252 in valid order", owned.Count == 252 && order.All(id => SkillData.ById[id].Requirements.All(r => order.IndexOf(r) < order.IndexOf(id))));

            // list filter: search + chip
            SearchBox.Text = "robot";
            Check("list search filters cards", skillVms.Count(v => v.Shown) is > 0 and < 252);
            SearchBox.Text = "";
            Check("list search cleared", skillVms.All(v => v.Shown));

            PermAllOn_Click(this, new RoutedEventArgs());
            SetPermanent([252], false);
            Check("permanent upgrades 20 of 21", permOrder.Count == 20 && !permOwned.Contains(252));
            SetStat("upgradePoints", 500);
            LtGambledBox.Text = "777";
            RunMoneyBox.Text = "123456";
            EndingSwitch.IsChecked = true; EndingSwitch_Click(EndingSwitch, new RoutedEventArgs());
            MailReceiveAll_Click(this, new RoutedEventArgs());
            MailReadAll_Click(this, new RoutedEventArgs());
            var offer = mailVms.First(m => m.Def.Key == "AnOfferYouCantRefuse");
            ChangeMails([offer], m => m.Accepted = true);
            Check("all mails received", mailVms.All(m => m.Received && m.Opened));

            FovSlider.Value = 75;
            BloodCombo.SelectedIndex = 2;
            InvertYSwitch.IsChecked = true; Setting_Changed(InvertYSwitch, new RoutedEventArgs());
            MasterSlider.Value = 40;

            bool saved = await SaveAll();
            Check("save ok", saved);
            string raw = File.ReadAllText(folder!.SavePath);
            var s2 = UnityJson.Parse(raw);
            Check("skills written", s2.GetIntList("unlockedSkills").Count == 252);
            Check("permanent written", s2.GetIntList("unlockedPermanentUpgrades").Count == 20);
            Check("points written", s2.GetLong("upgradePoints") == 500);
            Check("run money written", s2.GetLong("totalMoney") == 123456);
            Check("lifetime nested written", s2.GetPath("lifetimeStats.totalGambled") == 777 && raw.Contains("\n        \"totalGambled\": 777,"));
            Check("ending written", s2.GetBool("hasSeenEnding"));
            var mails = (JsonArray)s2.Root["mails"]!;
            Check("mails written", mails.Count == GameData.Mails.Count && mails.OfType<JsonObject>().Any(o => UnityJson.Long(o, "id") == offer.Id && UnityJson.Bool(o, "accepted")));
            Check("mail object format", raw.Contains("\n        {\n            \"id\": 0,\n            \"receivedDay\": "));
            string st = File.ReadAllText(folder.SettingsPath);
            Check("fov written as int", st.Contains("\"fieldOfView\": 75,"));
            Check("blood written", st.Contains("\"bloodIndex\": 2,"));
            Check("invertY written", st.Contains("\"invertY\": true,"));
            Check("master written as float", st.Contains("\"masterVolume\": 40.0"));
            Check("backup created", SaveStore.ListBackups(folder).Count >= 1);
            Check("no BOM", File.ReadAllBytes(folder.SavePath)[0] == (byte)'{');
            var before = Loc.Instance.Language;
            Loc.Instance.SetLanguage(UiLanguage.German);
            string? deText = SaveButton.Content as string;
            bool de = deText == Loc.Instance["SaveChanges"];
            Loc.Instance.SetLanguage(UiLanguage.English);
            bool en = SaveButton.Content as string == Loc.Instance["SaveChanges"] && SaveButton.Content as string != deText;
            Loc.Instance.SetLanguage(before);
            Check("XAML texts follow the language switch", de && en);
        }
        catch (Exception ex) { lines.Add("FAIL exception " + ex); }
        File.WriteAllLines(log, lines);
        Close();
    }
}
