using System.ComponentModel;
using System.Globalization;
using System.Resources;
using GitPersona.Core.Models;

namespace GitPersona.App.Localization;

/// <summary>
/// Serves UI strings from <c>Strings.resx</c> / <c>Strings.az.resx</c> and lets the language change at runtime:
/// XAML binds to the indexer (see <see cref="LocExtension"/>), which is refreshed on every switch.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly CultureInfo Azerbaijani = CultureInfo.GetCultureInfo("az-Latn-AZ");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly ResourceManager _resources =
        new("GitPersona.App.Localization.Strings", typeof(LocalizationManager).Assembly);

    private LocalizationManager()
    {
    }

    public static LocalizationManager Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public CultureInfo Culture { get; private set; } = SystemCulture();

    public string this[string key] => _resources.GetString(key, Culture) ?? $"[{key}]";

    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    public void SetLanguage(UiLanguage language)
    {
        Culture = language switch
        {
            UiLanguage.Azerbaijani => Azerbaijani,
            UiLanguage.English => English,
            _ => SystemCulture(),
        };
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static CultureInfo SystemCulture() =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "az" ? Azerbaijani : English;
}
