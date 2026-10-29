using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.App.Services;
using GitPersona.App.Views;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Models;
using GitPersona.Core.Profiles;
using GitPersona.Core.Ssh;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;

namespace GitPersona.App.ViewModels;

/// <summary>Edits one profile. Nothing is saved until <see cref="SaveCommand"/>; saving never writes git/ssh config by itself.</summary>
public sealed partial class ProfileEditorViewModel : LocalizedObject
{
    private static readonly HashSet<string> EditableProperties =
    [
        nameof(DisplayName), nameof(Id), nameof(GitUserName), nameof(GitUserEmail), nameof(GitHubUsername),
        nameof(Host), nameof(HostAlias), nameof(SshKeyPath), nameof(DefaultCloneFolder), nameof(Color), nameof(Icon),
    ];

    private readonly ProfilesService _profiles;
    private readonly SshKeyService _keys;
    private readonly DialogService _dialogs;
    private readonly UserPaths _paths;
    private readonly ILogger _logger;
    private Profile? _original;
    private bool _loading;
    private bool _silent;
    private bool _idTouched;
    private bool _aliasTouched;
    private SshKeyInfo? _keyInfo;
    private int _keyLookup;

    public ProfileEditorViewModel(Profile? original, ProfilesService profiles, SshKeyService keys, DialogService dialogs, UserPaths paths, ILogger logger)
    {
        _profiles = profiles;
        _keys = keys;
        _dialogs = dialogs;
        _paths = paths;
        _logger = logger;
        Folders.CollectionChanged += (_, _) => MarkDirty();
        Owners.CollectionChanged += (_, _) => MarkDirty();
        Load(original);
    }

    public event EventHandler<string>? Saved;

    public event EventHandler<string>? Deleted;

    public static IReadOnlyList<string> ColorPalette { get; } =
        ["#3B82F6", "#8B5CF6", "#EC4899", "#EF4444", "#F59E0B", "#10B981", "#14B8A6", "#64748B"];

    public static IReadOnlyList<SymbolRegular> IconPalette { get; } =
    [
        SymbolRegular.Person24, SymbolRegular.Briefcase24, SymbolRegular.Building24, SymbolRegular.Home24,
        SymbolRegular.Code24, SymbolRegular.Laptop24, SymbolRegular.Rocket24, SymbolRegular.Star24,
        SymbolRegular.Heart24, SymbolRegular.Globe24, SymbolRegular.BookOpen24, SymbolRegular.Beaker24,
    ];

    public bool IsNew => _original is null;

    public string Heading => IsNew && string.IsNullOrWhiteSpace(DisplayName) ? Loc["Editor_NewTitle"] : DisplayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial string Id { get; set; }

    [ObservableProperty]
    public partial string GitUserName { get; set; }

    [ObservableProperty]
    public partial string GitUserEmail { get; set; }

    [ObservableProperty]
    public partial string GitHubUsername { get; set; }

    [ObservableProperty]
    public partial string Host { get; set; }

    [ObservableProperty]
    public partial string HostAlias { get; set; }

    [ObservableProperty]
    public partial string SshKeyPath { get; set; }

    [ObservableProperty]
    public partial string DefaultCloneFolder { get; set; }

    [ObservableProperty]
    public partial string Color { get; set; }

    [ObservableProperty]
    public partial SymbolRegular Icon { get; set; }

    [ObservableProperty]
    public partial string NewOwner { get; set; }

    [ObservableProperty]
    public partial string? OwnerMessage { get; set; }

    [ObservableProperty]
    public partial string? KeyDescription { get; set; }

    [ObservableProperty]
    public partial string? KeyMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyPublicKeyCommand))]
    public partial bool HasPublicKey { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    public partial bool IsDirty { get; set; }

    public ObservableCollection<string> Folders { get; } = [];

    public ObservableCollection<string> Owners { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    /// <summary>Validates and saves the profile. Returns <see langword="false"/> and fills <see cref="Errors"/> on failure.</summary>
    public bool TrySave()
    {
        Errors.Clear();
        var profile = BuildProfile();
        var others = _profiles.Profiles.Where(p => p.Id != _original?.Id);
        var issues = ProfileValidator.Validate([.. others, profile]).Where(i => i.ProfileId == profile.Id).Distinct().ToList();
        if (issues.Count > 0)
        {
            foreach (var issue in issues)
            {
                Errors.Add(IssueText.For(issue));
            }

            return false;
        }

        try
        {
            _profiles.Save(profile, _original?.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Saving profile {Id} failed", profile.Id);
            Errors.Add(ex.Message);
            return false;
        }

        _original = profile;
        _idTouched = _aliasTouched = true;
        IsDirty = false;
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(Heading));
        DeleteCommand.NotifyCanExecuteChanged();
        Saved?.Invoke(this, profile.Id);
        return true;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } name && EditableProperties.Contains(name))
        {
            MarkDirty();
        }
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        _ = RefreshKeyAsync();
    }

    partial void OnDisplayNameChanged(string value)
    {
        if (!_loading && IsNew && !_idTouched)
        {
            Silently(() => Id = ProfileIds.Slugify(value));
        }
    }

    partial void OnIdChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        if (!_silent)
        {
            _idTouched = true;
        }

        if (!_aliasTouched)
        {
            Silently(() => HostAlias = value.Length > 0 ? Profile.DefaultAliasFor(value.Trim()) : string.Empty);
        }
    }

    partial void OnHostAliasChanged(string value)
    {
        if (!_loading && !_silent)
        {
            _aliasTouched = true;
        }
    }

    partial void OnSshKeyPathChanged(string value)
    {
        if (!_loading)
        {
            _ = RefreshKeyAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save() => TrySave();

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Revert() => Load(_original);

    private bool CanDelete => !IsNew;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (_original is not { } original)
        {
            return;
        }

        var choice = await _dialogs.AskAsync(
            Loc["Editor_DeleteTitle"],
            Loc.Format("Editor_DeleteConfirm", original.DisplayName),
            Loc["Common_Delete"],
            primaryAppearance: ControlAppearance.Danger);
        if (choice != DialogChoice.Primary)
        {
            return;
        }

        _profiles.Delete(original.Id);
        Deleted?.Invoke(this, original.Id);
    }

    [RelayCommand]
    private void AddFolder()
    {
        if (_dialogs.PickFolder(Folders.LastOrDefault()) is { } folder
            && !Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            Folders.Add(folder);
        }
    }

    [RelayCommand]
    private void RemoveFolder(string folder) => Folders.Remove(folder);

    [RelayCommand]
    private void BrowseCloneFolder()
    {
        if (_dialogs.PickFolder(string.IsNullOrEmpty(DefaultCloneFolder) ? Folders.FirstOrDefault() : DefaultCloneFolder) is { } folder)
        {
            DefaultCloneFolder = folder;
        }
    }

    [RelayCommand]
    private void AddOwner()
    {
        OwnerMessage = null;
        if (string.IsNullOrWhiteSpace(NewOwner))
        {
            return;
        }

        if (NormalizeOwner(NewOwner) is not { } owner)
        {
            OwnerMessage = Loc.Format("Owners_Invalid", NewOwner.Trim());
            return;
        }

        if (!Owners.Contains(owner, StringComparer.OrdinalIgnoreCase))
        {
            Owners.Add(owner);
        }

        NewOwner = string.Empty;
    }

    [RelayCommand]
    private void RemoveOwner(string owner) => Owners.Remove(owner);

    [RelayCommand]
    private void GenerateKey()
    {
        KeyMessage = null;
        var id = Id.Trim();
        if (!ProfileValidator.IsValidId(id))
        {
            KeyMessage = Loc["Key_NeedId"];
            return;
        }

        var viewModel = new GenerateKeyViewModel(_keys, _logger, FreeKeyPath(id), GitUserEmail.Trim());
        var window = new GenerateKeyWindow(viewModel) { Owner = DialogService.ActiveWindow };
        if (window.ShowDialog() == true && viewModel.Result is { } key)
        {
            SshKeyPath = key.PrivateKeyPath;
        }
    }

    [RelayCommand]
    private void ChooseKey()
    {
        KeyMessage = null;
        if (_dialogs.PickFile(Loc["Key_ChooseTitle"], Loc["Key_ChooseFilter"], _paths.SshDirectory) is not { } file)
        {
            return;
        }

        // Picking the .pub file is a common slip; the profile needs the private key next to it.
        SshKeyPath = file.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }

    [RelayCommand(CanExecute = nameof(HasPublicKey))]
    private void CopyPublicKey()
    {
        if (_keyInfo is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_keyInfo.PublicKey);
            KeyMessage = Loc["Key_Copied"];
        }
        catch (COMException ex)
        {
            KeyMessage = ex.Message;
        }
    }

    [MemberNotNull(nameof(DisplayName), nameof(Id), nameof(GitUserName), nameof(GitUserEmail), nameof(GitHubUsername),
        nameof(Host), nameof(HostAlias), nameof(SshKeyPath), nameof(DefaultCloneFolder), nameof(Color), nameof(NewOwner))]
    private void Load(Profile? profile)
    {
        _loading = true;
        DisplayName = profile?.DisplayName ?? string.Empty;
        Id = profile?.Id ?? string.Empty;
        GitUserName = profile?.GitUserName ?? string.Empty;
        GitUserEmail = profile?.GitUserEmail ?? string.Empty;
        GitHubUsername = profile?.GitHubUsername ?? string.Empty;
        Host = profile?.Host ?? Profile.DefaultHost;
        HostAlias = profile?.HostAlias ?? string.Empty;
        SshKeyPath = profile?.SshKeyPath ?? string.Empty;
        DefaultCloneFolder = profile?.DefaultCloneFolder ?? string.Empty;
        Color = profile?.Color ?? UnusedColor();
        Icon = Enum.TryParse<SymbolRegular>(profile?.Icon, out var icon) ? icon : SymbolRegular.Person24;
        NewOwner = string.Empty;
        OwnerMessage = null;
        KeyMessage = null;
        Folders.Clear();
        Owners.Clear();
        foreach (var folder in profile?.Folders ?? [])
        {
            Folders.Add(folder);
        }

        foreach (var owner in profile?.Owners ?? [])
        {
            Owners.Add(owner);
        }

        Errors.Clear();
        _original = profile;
        _idTouched = _aliasTouched = profile is not null;
        _loading = false;
        IsDirty = false;
        _ = RefreshKeyAsync();
    }

    private Profile BuildProfile() => new()
    {
        Id = Id.Trim(),
        DisplayName = DisplayName.Trim(),
        GitUserName = GitUserName.Trim(),
        GitUserEmail = GitUserEmail.Trim(),
        GitHubUsername = GitHubUsername.Trim(),
        Host = Host.Trim(),
        HostAlias = HostAlias.Trim(),
        SshKeyPath = SshKeyPath.Trim(),
        Folders = [.. Folders],
        Owners = [.. Owners],
        DefaultCloneFolder = string.IsNullOrWhiteSpace(DefaultCloneFolder) ? null : DefaultCloneFolder.Trim(),
        Color = Color,
        Icon = Icon.ToString(),
    };

    private async Task RefreshKeyAsync()
    {
        // Only the latest lookup may update the view; an older one finishing late is ignored.
        var lookup = ++_keyLookup;
        _keyInfo = null;
        HasPublicKey = false;

        var path = SshKeyPath.Trim();
        if (path.Length == 0)
        {
            KeyDescription = Loc["Key_None"];
            return;
        }

        if (!File.Exists(path))
        {
            KeyDescription = Loc["Key_NotFound"];
            return;
        }

        try
        {
            var info = await _keys.DescribeAsync(path);
            if (lookup != _keyLookup)
            {
                return;
            }

            _keyInfo = info;
            HasPublicKey = true;
            KeyDescription = string.Join(" · ", new[] { info.KeyType, info.Fingerprint, info.Comment }.Where(s => !string.IsNullOrEmpty(s)));
        }
        catch (Exception ex) when (ex is SshKeyException or ToolNotFoundException or ProcessRunException or IOException or UnauthorizedAccessException)
        {
            if (lookup == _keyLookup)
            {
                KeyDescription = Loc.Format("Key_Invalid", ex.Message);
            }
        }
    }

    private string FreeKeyPath(string id)
    {
        var path = _paths.DefaultSshKeyFile(id);
        for (var n = 2; File.Exists(path) || File.Exists(path + ".pub"); n++)
        {
            path = $"{_paths.DefaultSshKeyFile(id)}_{n}";
        }

        return path;
    }

    private string UnusedColor()
    {
        var used = _profiles.Profiles.Select(p => p.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ColorPalette.FirstOrDefault(c => !used.Contains(c)) ?? ColorPalette[0];
    }

    private void MarkDirty()
    {
        if (!_loading)
        {
            IsDirty = true;
        }
    }

    private void Silently(Action action)
    {
        _silent = true;
        try
        {
            action();
        }
        finally
        {
            _silent = false;
        }
    }

    /// <summary>Accepts <c>acme</c>, <c>@acme</c> or <c>https://github.com/acme/repo</c>; returns <c>acme</c>.</summary>
    internal static string? NormalizeOwner(string input)
    {
        var text = input.Trim().TrimStart('@');
        text = Regex.Replace(text, @"^(https?://)?(www\.)?[^/]*github[^/]*/", string.Empty, RegexOptions.IgnoreCase);
        text = text.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        // GitHub logins: alphanumerics and single inner hyphens, at most 39 characters.
        return Regex.IsMatch(text, "^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}$") ? text : null;
    }
}
