using System.Windows.Media;
using CanEmAllEditor.Core;

namespace CanEmAllEditor;

/// <summary>Bridges the UI-neutral core types to WPF.</summary>
static class WpfExtensions
{
    public static Color Wpf(this Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    extension(SkillDef d)
    {
        public string IconUri => $"pack://application:,,,/Assets/icons/{d.Icon}.png";
    }
}
