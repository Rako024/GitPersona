using CommunityToolkit.Mvvm.ComponentModel;

namespace GitPersona.App.Localization;

/// <summary>Base for view models that expose localized text: all bindings refresh when the language changes.</summary>
public abstract class LocalizedObject : ObservableObject
{
    protected LocalizedObject()
    {
        Loc.LanguageChanged += (_, _) => OnLanguageChanged();
    }

    protected static LocalizationManager Loc => LocalizationManager.Instance;

    /// <summary>Refreshes every binding by default.</summary>
    protected virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);
}

/// <summary>A choice in a combo box whose label is a resource key.</summary>
public sealed class LocalizedOption<T>(T value, string labelKey) : LocalizedObject
{
    public T Value { get; } = value;

    public string Label => Loc[labelKey];

    /// <summary>Screen readers and UI Automation read combo box items through ToString.</summary>
    public override string ToString() => Label;
}
