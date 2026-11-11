using System.Windows.Data;
using System.Windows.Markup;
using CanEmAllEditor.Core;

namespace CanEmAllEditor;

/// <summary>XAML shorthand for a live-updating translated text: Text="{l:T SaveChanges}".</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
