using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CanEmAllEditor.Core;

namespace CanEmAllEditor;

/// <summary>
/// The skill tree drawn with the game's own node layout: pan with drag, zoom with the wheel,
/// click a node to select it, double-click (or click the selected one again) to toggle it.
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

    Point dragStart, panStart;
    bool dragging, moved;
    int? pressedId;

    public Func<int, bool> IsOwned { get; set; } = _ => false;
    public int? SelectedId { get; private set; }

    /// <summary>Raised when the user picks a node.</summary>
    public event Action<int>? NodeSelected;

    /// <summary>Raised when the user wants a node toggled (double click).</summary>
    public event Action<int>? NodeToggled;

    sealed class NodeVisual
    {
        public required Grid Root;
        public required Shape Body;
        public required Image Icon;
        public required Shape Ring;
        public required SkillDef Def;
    }

    public SkillTreeControl()
    {
        ClipToBounds = true;
        Background = new RadialGradientBrush(Color.FromRgb(0x1A, 0x16, 0x2C), Color.FromRgb(0x0B, 0x0D, 0x13)) { RadiusX = 0.8, RadiusY = 0.8 };
        CornerRadius = new CornerRadius(12);
        BorderBrush = (Brush)Application.Current.FindResource("Line");
        BorderThickness = new Thickness(1);
        Cursor = Cursors.SizeAll;

        var all = SkillData.All;
        minX = all.Min(s => s.X);
        maxY = all.Max(s => s.Y);
        width = all.Max(s => s.X) - minX + 2 * Margin_;
        height = maxY - all.Min(s => s.Y) + 2 * Margin_;

        canvas.Width = width;
        canvas.Height = height;
        canvas.RenderTransform = new TransformGroup { Children = { scale, translate } };
        canvas.Background = Brushes.Transparent;
        DrawGrid();
        BuildLines();
        BuildNodes();

        var host = new Grid { Background = Brushes.Transparent };
        host.Children.Add(canvas);
        Child = host;

        SizeChanged += (_, e) => { if (e.PreviousSize.Width == 0) Fit(); };
        MouseWheel += OnWheel;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseLeave += (_, _) => dragging = false;
        MouseRightButtonUp += (_, e) =>
        {
            if (NodeAt(e.GetPosition(this)) is int id)
            {
                Select(id);
                NodeSelected?.Invoke(id);
                NodeToggled?.Invoke(id);
            }
        };
    }

    Point Pos(SkillDef s) => new(s.X - minX + Margin_, maxY - s.Y + Margin_);

    void DrawGrid()
    {
        var dot = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        dot.Freeze();
        var geo = new GeometryGroup();
        for (double x = 0; x < width; x += 45)
            for (double y = 0; y < height; y += 45)
                geo.Children.Add(new EllipseGeometry(new Point(x + 35, y + 35), 1.2, 1.2));
        geo.Freeze();
        canvas.Children.Add(new Path { Data = geo, Fill = dot, IsHitTestVisible = false });
    }

    void BuildLines()
    {
        foreach (var s in SkillData.All)
            foreach (int r in s.Requirements)
            {
                if (!SkillData.ById.TryGetValue(r, out var req)) continue;
                Point a = Pos(req), b = Pos(s);
                var line = new Line
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                    StrokeThickness = 4,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
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
            Shape body = s.Special
                ? new Rectangle { Width = size, Height = size, RadiusX = 12, RadiusY = 12, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45) }
                : new Ellipse { Width = size, Height = size };
            body.StrokeThickness = 3;

            Shape ring = s.Special
                ? new Rectangle { Width = size + 14, Height = size + 14, RadiusX = 16, RadiusY = 16, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45) }
                : new Ellipse { Width = size + 14, Height = size + 14 };
            ring.Stroke = (Brush)Application.Current.FindResource("Accent");
            ring.StrokeThickness = 3;
            ring.Visibility = Visibility.Collapsed;
            ring.Effect = new DropShadowEffect { Color = Color.FromRgb(0x8B, 0x5C, 0xF6), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 };

            var icon = new Image
            {
                Source = LoadIcon(s.IconUri),
                Width = Node * 0.56, Height = Node * 0.56,
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

            var root = new Grid { Width = Node + 16, Height = Node + 16, Tag = s.Id, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            root.Children.Add(ring);
            root.Children.Add(body);
            root.Children.Add(icon);
            ring.HorizontalAlignment = body.HorizontalAlignment = icon.HorizontalAlignment = HorizontalAlignment.Center;
            ring.VerticalAlignment = body.VerticalAlignment = icon.VerticalAlignment = VerticalAlignment.Center;

            if (s.DemoLocked)
            {
                var badge = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x2E, 0x12)),
                    BorderBrush = (Brush)Application.Current.FindResource("Warn"), BorderThickness = new Thickness(1.5),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 2, 0), IsHitTestVisible = false,
                    Child = new Image { Source = LoadIcon("pack://application:,,,/Assets/icons/locked.png"), Width = 12, Height = 12 },
                };
                root.Children.Add(badge);
            }

            root.ToolTip = BuildTooltip(s);
            ToolTipService.SetInitialShowDelay(root, 250);
            root.MouseEnter += (_, _) => root.RenderTransform = new ScaleTransform(1.12, 1.12, root.Width / 2, root.Height / 2);
            root.MouseLeave += (_, _) => root.RenderTransform = null;

            Point p = Pos(s);
            Canvas.SetLeft(root, p.X - root.Width / 2);
            Canvas.SetTop(root, p.Y - root.Height / 2);
            canvas.Children.Add(root);
            nodes[s.Id] = new NodeVisual { Root = root, Body = body, Icon = icon, Ring = ring, Def = s };
        }
    }

    static readonly Dictionary<string, ImageSource> iconCache = [];
    static ImageSource LoadIcon(string uri)
    {
        if (iconCache.TryGetValue(uri, out var img)) return img;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(uri);
        bmp.DecodePixelWidth = 96;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return iconCache[uri] = bmp;
    }

    object BuildTooltip(SkillDef s)
    {
        var tb = new TextBlock { MaxWidth = 280, TextWrapping = TextWrapping.Wrap };
        tb.Loaded += (_, _) =>
        {
            tb.Inlines.Clear();
            tb.Inlines.Add(new System.Windows.Documents.Run(s.Name) { FontWeight = FontWeights.Bold, FontSize = 14 });
            tb.Inlines.Add(new System.Windows.Documents.LineBreak());
            tb.Inlines.Add(new System.Windows.Documents.Run(SkillData.LabelOf(s.Category)) { Foreground = new SolidColorBrush(SkillData.ColorOf(s.Category).Wpf()), FontSize = 11 });
            tb.Inlines.Add(new System.Windows.Documents.LineBreak());
            tb.Inlines.Add(new System.Windows.Documents.Run(s.Description));
            tb.Inlines.Add(new System.Windows.Documents.LineBreak());
            tb.Inlines.Add(new System.Windows.Documents.Run("$ " + Loc.Num(s.Cost)) { Foreground = (Brush)Application.Current.FindResource("Gold"), FontWeight = FontWeights.SemiBold });
            if (s.DemoLocked)
            {
                tb.Inlines.Add(new System.Windows.Documents.LineBreak());
                tb.Inlines.Add(new System.Windows.Documents.Run("🔒 " + Loc.T("DemoLocked")) { Foreground = (Brush)Application.Current.FindResource("Warn") });
            }
        };
        return tb;
    }

    /// <summary>Recolors every node and line from the current owned state.</summary>
    public void RefreshAll()
    {
        var line = (Brush)Application.Current.FindResource("Line");
        foreach (var (l, from, to) in lines)
        {
            bool a = IsOwned(from.Id), b = IsOwned(to.Id);
            if (a && b)
            {
                l.Stroke = new SolidColorBrush(SkillData.ColorOf(to.Category).Wpf());
                l.Opacity = 0.95;
                l.StrokeDashArray = null;
            }
            else if (a)
            {
                l.Stroke = new SolidColorBrush(SkillData.ColorOf(to.Category).Wpf());
                l.Opacity = 0.45;
                l.StrokeDashArray = [1.5, 1.5];
            }
            else
            {
                l.Stroke = line;
                l.Opacity = 0.9;
                l.StrokeDashArray = null;
            }
        }

        foreach (var n in nodes.Values)
        {
            var c = SkillData.ColorOf(n.Def.Category).Wpf();
            bool owned = IsOwned(n.Def.Id);
            bool available = !owned && n.Def.Requirements.All(IsOwned);
            if (owned)
            {
                n.Body.Fill = new LinearGradientBrush(Lighten(c, 0.15), Darken(c, 0.25), 90);
                n.Body.Stroke = new SolidColorBrush(Color.FromRgb(0xF5, 0xE6, 0xCC));
                n.Icon.Opacity = 1;
                n.Body.Effect = new DropShadowEffect { Color = c, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.55 };
            }
            else if (available)
            {
                n.Body.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x36));
                n.Body.Stroke = new SolidColorBrush(c);
                n.Icon.Opacity = 0.8;
                n.Body.Effect = null;
            }
            else
            {
                n.Body.Fill = new SolidColorBrush(Color.FromRgb(0x15, 0x17, 0x20));
                n.Body.Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x4A));
                n.Icon.Opacity = 0.3;
                n.Body.Effect = null;
            }
            n.Ring.Visibility = SelectedId == n.Def.Id ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    static Color Lighten(Color c, double f) => Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
    static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));

    public void Select(int id, bool center = false)
    {
        SelectedId = id;
        foreach (var n in nodes.Values) n.Ring.Visibility = n.Def.Id == id ? Visibility.Visible : Visibility.Collapsed;
        if (center && nodes.TryGetValue(id, out var nv)) CenterOn(Pos(nv.Def));
    }

    void CenterOn(Point p)
    {
        translate.X = ActualWidth / 2 - p.X * scale.ScaleX;
        translate.Y = ActualHeight / 2 - p.Y * scale.ScaleY;
    }

    public void Fit()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        double z = Math.Min(ActualWidth / width, ActualHeight / height);
        scale.ScaleX = scale.ScaleY = z;
        translate.X = (ActualWidth - width * z) / 2;
        translate.Y = (ActualHeight - height * z) / 2;
    }

    public void Zoom(double factor) => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor);

    void ZoomAt(Point at, double factor)
    {
        double z = Math.Clamp(scale.ScaleX * factor, 0.15, 3.0);
        factor = z / scale.ScaleX;
        translate.X = at.X - (at.X - translate.X) * factor;
        translate.Y = at.Y - (at.Y - translate.Y) * factor;
        scale.ScaleX = scale.ScaleY = z;
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    int? NodeAt(Point p)
    {
        DependencyObject? hit = InputHitTest(p) as DependencyObject;
        while (hit is not null && hit != this)
        {
            if (hit is Grid g && g.Tag is int id) return id;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(this);
        panStart = new Point(translate.X, translate.Y);
        dragging = true;
        moved = false;
        pressedId = NodeAt(dragStart);
        CaptureMouse();
        if (e.ClickCount == 2 && pressedId is int id)
        {
            NodeToggled?.Invoke(id);
            dragging = false;
            ReleaseMouseCapture();
        }
        e.Handled = true;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        var p = e.GetPosition(this);
        var d = p - dragStart;
        if (!moved && d.Length < 4) return;
        moved = true;
        translate.X = panStart.X + d.X;
        translate.Y = panStart.Y + d.Y;
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        ReleaseMouseCapture();
        if (!moved && pressedId is int id)
        {
            Select(id);
            NodeSelected?.Invoke(id);
        }
    }
}
