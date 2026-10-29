using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.App.Services;
using GitPersona.App.ViewModels;
using GitPersona.Core.GitHub;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace GitPersona.App.Tray;

/// <summary>The notification-area icon and its menu.</summary>
public sealed class TrayService(AppShell shell, ProfilesService profiles, GhCliService gh, ILogger<TrayService> logger) : IDisposable
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

        menu.Items.Add(Item("Tray_CloneFromUrl", SymbolRegular.ArrowDownload24, shell.ShowClone));
        menu.Items.Add(Item("Tray_Scan", SymbolRegular.FolderSearch24, shell.ShowScanner));

        if (gh.IsInstalled)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(GhMenu());
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Tray_Exit", SymbolRegular.Power24, shell.Exit));
        return menu;
    }

    /// <summary>gh's accounts, read when the submenu opens so the tray never waits for gh.</summary>
    private MenuItem GhMenu()
    {
        var root = new MenuItem { Header = Loc["Tray_GhAccount"], Icon = new SymbolIcon(SymbolRegular.PersonSwap24) };
        root.Items.Add(new MenuItem { Header = Loc["Tray_GhLoading"], IsEnabled = false });
        root.SubmenuOpened += async (_, e) =>
        {
            if (e.OriginalSource != root)
            {
                return;
            }

            var accounts = await gh.ListAccountsAsync();
            root.Items.Clear();
            foreach (var account in accounts)
            {
                var entry = new MenuItem
                {
                    Header = account.Host == "github.com" ? account.Login : $"{account.Login}  ·  {account.Host}",
                    IsCheckable = false,
                    IsChecked = account.Active,
                };
                entry.Click += async (_, _) => await SwitchAsync(account);
                root.Items.Add(entry);
            }

            if (root.Items.Count == 0)
            {
                root.Items.Add(new MenuItem { Header = Loc["Tray_GhNone"], IsEnabled = false });
            }
        };
        return root;
    }

    private async Task SwitchAsync(GhAccount account)
    {
        if (account.Active)
        {
            return;
        }

        try
        {
            await gh.SwitchAsync(account);
            ShowNotification("GitPersona", Loc.Format("Tray_GhSwitched", account.Login));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ProcessRunException or ToolNotFoundException)
        {
            logger.LogError(ex, "gh auth switch to {Login} failed", account.Login);
            ShowNotification("GitPersona", ex.Message);
        }
    }

    private static MenuItem Item(string key, SymbolRegular icon, Action action)
    {
        var item = new MenuItem { Header = Loc[key], Icon = new SymbolIcon(icon) };
        item.Click += (_, _) => action();
        return item;
    }
}
