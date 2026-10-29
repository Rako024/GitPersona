using CommunityToolkit.Mvvm.ComponentModel;
using GitPersona.App.Localization;
using Wpf.Ui.Controls;

namespace GitPersona.App.ViewModels;

public enum AppPage
{
    Profiles,
    Clone,
    Scanner,
    Diagnostics,
    Settings,
}

public sealed class NavigationItem(AppPage page, string titleKey, SymbolRegular icon, object content) : LocalizedObject
{
    public AppPage Page { get; } = page;

    public SymbolRegular Icon { get; } = icon;

    public string Title => Loc[titleKey];

    /// <summary>Page view model; the matching view is picked by a DataTemplate in App.xaml.</summary>
    public object Content { get; } = content;

    public override string ToString() => Title;
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(ProfilesViewModel profiles, CloneViewModel clone, ScannerViewModel scanner, SettingsViewModel settings)
    {
        Items =
        [
            new(AppPage.Profiles, "Nav_Profiles", SymbolRegular.People24, profiles),
            new(AppPage.Clone, "Nav_Clone", SymbolRegular.ArrowDownload24, clone),
            new(AppPage.Scanner, "Nav_Scanner", SymbolRegular.FolderSearch24, scanner),
            new(AppPage.Diagnostics, "Nav_Diagnostics", SymbolRegular.Stethoscope24,
                new PlaceholderPageViewModel("Diagnostics_Description", phase: 7)),
            new(AppPage.Settings, "Nav_Settings", SymbolRegular.Settings24, settings),
        ];
        SelectedItem = Items[0];
    }

    public IReadOnlyList<NavigationItem> Items { get; }

    [ObservableProperty]
    public partial NavigationItem? SelectedItem { get; set; }

    public void NavigateTo(AppPage page) => SelectedItem = Items.First(item => item.Page == page);

    partial void OnSelectedItemChanged(NavigationItem? value) => (value?.Content as IPageActivation)?.OnNavigatedTo();
}
