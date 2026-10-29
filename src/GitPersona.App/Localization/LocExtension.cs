using System.Windows.Data;
using System.Windows.Markup;

namespace GitPersona.App.Localization;

/// <summary>
/// <c>Text="{l:Loc Settings_Theme}"</c> — a binding to the localized string that follows language switches.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}
