using GitPersona.App.Localization;

namespace GitPersona.App.ViewModels;

/// <summary>Stands in for a page that a later phase implements.</summary>
public sealed class PlaceholderPageViewModel(string descriptionKey, int phase) : LocalizedObject
{
    public string Description => Loc[descriptionKey];

    public string Title => Loc["Placeholder_Title"];

    public string PhaseNote => Loc.Format("Placeholder_Phase", phase);
}
