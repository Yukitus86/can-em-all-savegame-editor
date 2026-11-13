namespace CanEmAllEditor.Core;

/// <summary>UI-neutral color, so the core works for the WPF (Windows) and the Avalonia (Linux) front end.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromRgb(byte r, byte g, byte b) => new(r, g, b);
}
