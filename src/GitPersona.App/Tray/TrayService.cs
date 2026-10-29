using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.App.Services;
using GitPersona.App.ViewModels;
using H.NotifyIcon;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace GitPersona.App.Tray;

/// <summary>The notification-area icon and its menu.</summary>
public sealed class TrayService(AppShell shell, ProfilesService profiles) : IDisposable
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    private TaskbarIcon? _icon;

    public void Initialize()
    {
        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/GitPersona.ico")),
            ToolTipText = "GitPersona",
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(shell.ShowMainWindow),
            ContextMenu = BuildMenu(),
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
        Loc.LanguageChanged += RebuildMenu;
        profiles.Changed += RebuildMenu;
    }

    public void ShowNotification(string title, string message) => _icon?.ShowNotification(title, message);

    public void Dispose()
    {
        Loc.LanguageChanged -= RebuildMenu;
        profiles.Changed -= RebuildMenu;
        _icon?.Dispose();
        _icon = null;
    }

    private void RebuildMenu(object? sender, EventArgs e)
    {
        if (_icon is not null)
        {
            _icon.ContextMenu = BuildMenu();
        }
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Tray_Open", SymbolRegular.Open24, shell.ShowMainWindow));
        menu.Items.Add(new Separator());

        var profileMenu = Item("Tray_Profiles", SymbolRegular.People24, () => shell.Navigate(AppPage.Profiles));
        foreach (var profile in profiles.Profiles)
        {
            var icon = Enum.TryParse<SymbolRegular>(profile.Icon, out var symbol) ? symbol : SymbolRegular.Person24;
            var id = profile.Id;
            var entry = new MenuItem { Header = $"{profile.DisplayName}  ·  {profile.GitUserEmail}", Icon = new SymbolIcon(icon) };
            entry.Click += (_, _) => shell.ShowProfile(id);
            profileMenu.Items.Add(entry);
        }

        if (profileMenu.Items.Count == 0)
        {
            profileMenu.Items.Add(new MenuItem { Header = Loc["Tray_NoProfiles"], IsEnabled = false });
        }

        menu.Items.Add(profileMenu);

        menu.Items.Add(Item("Tray_CloneFromUrl", SymbolRegular.ArrowDownload24, () => shell.Navigate(AppPage.Clone)));
        menu.Items.Add(Item("Tray_Scan", SymbolRegular.FolderSearch24, () => shell.Navigate(AppPage.Scanner)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Tray_Exit", SymbolRegular.Power24, shell.Exit));
        return menu;
    }

    private static MenuItem Item(string key, SymbolRegular icon, Action action)
    {
        var item = new MenuItem { Header = Loc[key], Icon = new SymbolIcon(icon) };
        item.Click += (_, _) => action();
        return item;
    }
}
