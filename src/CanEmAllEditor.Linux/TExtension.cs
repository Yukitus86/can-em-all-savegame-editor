using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using CanEmAllEditor.Core;

namespace CanEmAllEditor;

/// <summary>XAML shorthand for a live-updating translated text: Text="{l:T SaveChanges}".</summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    // Avalonia does not refresh indexer bindings on "Item[]", so each key gets a small object with a plain property.
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(LocText.Value)) { Source = LocText.For(Key), Mode = BindingMode.OneWay };
}

/// <summary>One translated text that raises PropertyChanged when the UI language changes.</summary>
public sealed class LocText : INotifyPropertyChanged
{
    static readonly Dictionary<string, LocText> cache = [];

    readonly string key;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Value => Loc.Instance[key];

    LocText(string key)
    {
        this.key = key;
        Loc.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Loc.Language)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        };
    }

    public static LocText For(string key) => cache.TryGetValue(key, out var t) ? t : cache[key] = new LocText(key);
}
