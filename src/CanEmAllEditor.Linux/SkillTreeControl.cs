using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using CanEmAllEditor.Core;

namespace CanEmAllEditor;

/// <summary>
/// The skill tree drawn with the game's own node layout: pan with drag, zoom with the wheel,
/// click a node to select it, double-click or right-click to toggle it.
/// </summary>
public sealed class SkillTreeControl : Border
{
    const double Node = 58;
    const double Margin_ = 80;

    readonly Canvas canvas = new();
    readonly ScaleTransform scale = new(1, 1);
    readonly TranslateTransform translate = new();
    readonly Dictionary<int, NodeVisual> nodes = [];
    readonly List<(Line line, SkillDef from, SkillDef to)> lines = [];
    readonly double minX, maxY, width, height;
    UiLanguage? tooltipLanguage;
    bool fitted;

    Point dragStart, panStart;
    bool dragging, moved;
    int? pressedId;

    public Func<int, bool> IsOwned { get; set; } = _ => false;
    public int? SelectedId { get; private set; }

    /// <summary>Raised when the user picks a node.</summary>
    public event Action<int>? NodeSelected;

    /// <summary>Raised when the user wants a node toggled (double click / right click).</summary>
    public event Action<int>? NodeToggled;

    sealed class NodeVisual
    {
        public required Panel Root;
        public required Border Body;
        public required Image Icon;
        public required Border Ring;
        public required SkillDef Def;
    }

    static readonly IBrush LineBrush = new ImmutableSolidColorBrush(Color.Parse("#2A3042"));
    static readonly IBrush AccentBrush = new ImmutableSolidColorBrush(Color.Parse("#8B5CF6"));
    static readonly IBrush WarnBrush = new ImmutableSolidColorBrush(Color.Parse("#FBBF24"));
    static readonly IBrush GoldBrush = new ImmutableSolidColorBrush(Color.Parse("#FFD36B"));
    static readonly IBrush Cream = new ImmutableSolidColorBrush(Color.FromRgb(0xF5, 0xE6, 0xCC));
    static readonly IBrush AvailableFill = new ImmutableSolidColorBrush(Color.FromRgb(0x22, 0x26, 0x36));
    static readonly IBrush LockedFill = new ImmutableSolidColorBrush(Color.FromRgb(0x15, 0x17, 0x20));
    static readonly IBrush LockedStroke = new ImmutableSolidColorBrush(Color.FromRgb(0x33, 0x38, 0x4A));

    public SkillTreeControl()
    {
        ClipToBounds = true;
        Background = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromRgb(0x1A, 0x16, 0x2C), 0), new GradientStop(Color.FromRgb(0x0B, 0x0D, 0x13), 1) },
            RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
        };
        CornerRadius = new CornerRadius(12);
        BorderBrush = LineBrush;
        BorderThickness = new Thickness(1);
        Cursor = new Cursor(StandardCursorType.SizeAll);

        var all = SkillData.All;
        minX = all.Min(s => s.X);
        maxY = all.Max(s => s.Y);
        width = all.Max(s => s.X) - minX + 2 * Margin_;
        height = maxY - all.Min(s => s.Y) + 2 * Margin_;

        canvas.Width = width;
        canvas.Height = height;
        canvas.HorizontalAlignment = HorizontalAlignment.Left;
        canvas.VerticalAlignment = VerticalAlignment.Top;
        canvas.RenderTransformOrigin = RelativePoint.TopLeft;
        canvas.RenderTransform = new TransformGroup { Children = { scale, translate } };
        canvas.Background = Brushes.Transparent;
        canvas.Children.Add(new DotGrid { Width = width, Height = height, IsHitTestVisible = false });
        BuildLines();
        BuildNodes();

        var host = new Panel { Background = Brushes.Transparent };
        host.Children.Add(canvas);
        Child = host;

        SizeChanged += (_, _) => { if (!fitted && Bounds.Width > 0) { fitted = true; Fit(); } };
        PointerWheelChanged += OnWheel;
        PointerPressed += OnDown;
        PointerMoved += OnMove;
        PointerReleased += OnUp;
        PointerCaptureLost += (_, _) => dragging = false;
    }

    /// <summary>Dotted background, drawn in one pass.</summary>
    sealed class DotGrid : Control
    {
        static readonly IBrush Dot = new ImmutableSolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

        public override void Render(DrawingContext context)
        {
            for (double x = 0; x < Bounds.Width; x += 45)
                for (double y = 0; y < Bounds.Height; y += 45)
                    context.DrawEllipse(Dot, null, new Point(x + 35, y + 35), 1.2, 1.2);
        }
    }

    Point Pos(SkillDef s) => new(s.X - minX + Margin_, maxY - s.Y + Margin_);

    void BuildLines()
    {
        foreach (var s in SkillData.All)
            foreach (int r in s.Requirements)
            {
                if (!SkillData.ById.TryGetValue(r, out var req)) continue;
                var line = new Line
                {
                    StartPoint = Pos(req),
                    EndPoint = Pos(s),
                    StrokeThickness = 4,
                    StrokeLineCap = PenLineCap.Round,
                    IsHitTestVisible = false,
                };
                lines.Add((line, req, s));
                canvas.Children.Add(line);
            }
    }

    void BuildNodes()
    {
        foreach (var s in SkillData.All)
        {
            double size = s.Special ? Node * 0.80 : Node;
            // circle for normal skills, rotated rounded square (diamond) for special ones
            var body = new Border
            {
                Width = size, Height = size,
                CornerRadius = new CornerRadius(s.Special ? 12 : size / 2),
                BorderThickness = new Thickness(3),
                RenderTransform = s.Special ? new RotateTransform(45) : null,
            };
            var ring = new Border
            {
                Width = size + 14, Height = size + 14,
                CornerRadius = new CornerRadius(s.Special ? 16 : (size + 14) / 2),
                BorderThickness = new Thickness(3),
                BorderBrush = AccentBrush,
                RenderTransform = s.Special ? new RotateTransform(45) : null,
                BoxShadow = BoxShadows.Parse("0 0 18 0 #E68B5CF6"),
                IsVisible = false,
            };
            var icon = new Image
            {
                Source = Ui.Icon(s.Icon),
                Width = Node * 0.56, Height = Node * 0.56,
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapInterpolationMode(icon, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);

            var root = new Panel { Width = Node + 16, Height = Node + 16, Tag = s.Id, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            root.Children.Add(ring);
            root.Children.Add(body);
            root.Children.Add(icon);
            ring.HorizontalAlignment = body.HorizontalAlignment = icon.HorizontalAlignment = HorizontalAlignment.Center;
            ring.VerticalAlignment = body.VerticalAlignment = icon.VerticalAlignment = VerticalAlignment.Center;

            if (s.DemoLocked)
            {
                root.Children.Add(new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
                    Background = new ImmutableSolidColorBrush(Color.FromRgb(0x3A, 0x2E, 0x12)),
                    BorderBrush = WarnBrush, BorderThickness = new Thickness(1.5),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 2, 0), IsHitTestVisible = false,
                    Child = new Image { Source = Ui.Icon("locked"), Width = 12, Height = 12 },
                });
            }

            ToolTip.SetShowDelay(root, 250);
            root.PointerEntered += (_, _) => root.RenderTransform = new ScaleTransform(1.12, 1.12);
            root.PointerExited += (_, _) => root.RenderTransform = null;

            Point p = Pos(s);
            Canvas.SetLeft(root, p.X - root.Width / 2);
            Canvas.SetTop(root, p.Y - root.Height / 2);
            canvas.Children.Add(root);
            nodes[s.Id] = new NodeVisual { Root = root, Body = body, Icon = icon, Ring = ring, Def = s };
        }
    }

    static TextBlock BuildTooltip(SkillDef s)
    {
        var tb = new TextBlock { MaxWidth = 280, TextWrapping = TextWrapping.Wrap };
        tb.Inlines!.Add(new Run(s.Name) { FontWeight = FontWeight.Bold, FontSize = 14 });
        tb.Inlines.Add(new LineBreak());
        tb.Inlines.Add(new Run(SkillData.LabelOf(s.Category)) { Foreground = SkillData.ColorOf(s.Category).Brush(), FontSize = 11 });
        tb.Inlines.Add(new LineBreak());
        tb.Inlines.Add(new Run(s.Description));
        tb.Inlines.Add(new LineBreak());
        tb.Inlines.Add(new Run("$ " + Loc.Num(s.Cost)) { Foreground = GoldBrush, FontWeight = FontWeight.SemiBold });
        if (s.DemoLocked)
        {
            tb.Inlines.Add(new LineBreak());
            tb.Inlines.Add(new Run("🔒 " + Loc.T("DemoLocked")) { Foreground = WarnBrush });
        }
        return tb;
    }

    /// <summary>Recolors every node and line from the current owned state.</summary>
    public void RefreshAll()
    {
        if (tooltipLanguage != Loc.Instance.Language)
        {
            tooltipLanguage = Loc.Instance.Language;
            foreach (var n in nodes.Values) ToolTip.SetTip(n.Root, BuildTooltip(n.Def));
        }

        foreach (var (l, from, to) in lines)
        {
            bool a = IsOwned(from.Id), b = IsOwned(to.Id);
            if (a && b)
            {
                l.Stroke = SkillData.ColorOf(to.Category).Brush();
                l.Opacity = 0.95;
                l.StrokeDashArray = null;
            }
            else if (a)
            {
                l.Stroke = SkillData.ColorOf(to.Category).Brush();
                l.Opacity = 0.45;
                l.StrokeDashArray = new AvaloniaList<double> { 1.5, 1.5 };
            }
            else
            {
                l.Stroke = LineBrush;
                l.Opacity = 0.9;
                l.StrokeDashArray = null;
            }
        }

        foreach (var n in nodes.Values)
        {
            var c = SkillData.ColorOf(n.Def.Category).Av();
            bool owned = IsOwned(n.Def.Id);
            bool available = !owned && n.Def.Requirements.All(IsOwned);
            if (owned)
            {
                n.Body.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Lighten(c, 0.15), 0), new GradientStop(Darken(c, 0.25), 1) },
                };
                n.Body.BorderBrush = Cream;
                n.Icon.Opacity = 1;
                n.Body.BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(0x8C, c.R, c.G, c.B) });
            }
            else if (available)
            {
                n.Body.Background = AvailableFill;
                n.Body.BorderBrush = new ImmutableSolidColorBrush(c);
                n.Icon.Opacity = 0.8;
                n.Body.BoxShadow = default;
            }
            else
            {
                n.Body.Background = LockedFill;
                n.Body.BorderBrush = LockedStroke;
                n.Icon.Opacity = 0.3;
                n.Body.BoxShadow = default;
            }
            n.Ring.IsVisible = SelectedId == n.Def.Id;
        }
    }

    static Color Lighten(Color c, double f) => Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
    static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));

    public void Select(int id, bool center = false)
    {
        SelectedId = id;
        foreach (var n in nodes.Values) n.Ring.IsVisible = n.Def.Id == id;
        if (center && nodes.TryGetValue(id, out var nv)) CenterOn(Pos(nv.Def));
    }

    void CenterOn(Point p)
    {
        translate.X = Bounds.Width / 2 - p.X * scale.ScaleX;
        translate.Y = Bounds.Height / 2 - p.Y * scale.ScaleY;
    }

    public void Fit()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double z = Math.Min(Bounds.Width / width, Bounds.Height / height);
        scale.ScaleX = scale.ScaleY = z;
        translate.X = (Bounds.Width - width * z) / 2;
        translate.Y = (Bounds.Height - height * z) / 2;
    }

    public void Zoom(double factor) => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

    void ZoomAt(Point at, double factor)
    {
        double z = Math.Clamp(scale.ScaleX * factor, 0.15, 3.0);
        factor = z / scale.ScaleX;
        translate.X = at.X - (at.X - translate.X) * factor;
        translate.Y = at.Y - (at.Y - translate.Y) * factor;
        scale.ScaleX = scale.ScaleY = z;
    }

    void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    static int? NodeOf(object? source)
    {
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
            if (v is Panel { Tag: int id }) return id;
        return null;
    }

    void OnDown(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        pressedId = NodeOf(e.Source);
        if (point.Properties.IsRightButtonPressed)
        {
            if (pressedId is int rid)
            {
                Select(rid);
                NodeSelected?.Invoke(rid);
                NodeToggled?.Invoke(rid);
            }
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        dragStart = e.GetPosition(this);
        panStart = new Point(translate.X, translate.Y);
        dragging = true;
        moved = false;
        e.Pointer.Capture(this);
        if (e.ClickCount == 2 && pressedId is int id)
        {
            NodeToggled?.Invoke(id);
            dragging = false;
            e.Pointer.Capture(null);
        }
        e.Handled = true;
    }

    void OnMove(object? sender, PointerEventArgs e)
    {
        if (!dragging) return;
        var p = e.GetPosition(this);
        var d = p - dragStart;
        if (!moved && Math.Sqrt(d.X * d.X + d.Y * d.Y) < 4) return;
        moved = true;
        translate.X = panStart.X + d.X;
        translate.Y = panStart.Y + d.Y;
    }

    void OnUp(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        e.Pointer.Capture(null);
        if (!moved && pressedId is int id)
        {
            Select(id);
            NodeSelected?.Invoke(id);
        }
    }
}
