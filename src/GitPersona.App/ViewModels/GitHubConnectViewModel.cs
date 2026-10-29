using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.Core.GitHub;
using GitPersona.Core.Security;
using Microsoft.Extensions.Logging;

namespace GitPersona.App.ViewModels;

public enum ConnectStep
{
    ChooseMethod,
    DeviceCode,
    Token,
    Working,
    Mismatch,
    Done,
}

/// <param name="Email">The commit email the user picked, if any.</param>
public sealed record ConnectResult(string Login, string? Email, string Summary);

public sealed record EmailOption(string Email, string Label);

/// <summary>
/// Signs in to GitHub (device flow or token), refuses to silently use the wrong account,
/// uploads the profile's public key and offers the account's verified emails.
/// </summary>
public sealed partial class GitHubConnectViewModel : LocalizedObject, IDisposable
{
    private readonly GitHubApiService _api;
    private readonly GitHubTokenStore _tokens;
    private readonly ILogger _logger;
    private readonly string _host;
    private readonly string _publicKey;
    private readonly string _keyTitle;
    private readonly string _currentEmail;
    private readonly string? _clientId;
    private readonly CancellationTokenSource _cancellation = new();
    private string? _expectedLogin;
    private string? _token;
    private GitHubIdentity? _identity;

    public GitHubConnectViewModel(
        GitHubApiService api,
        GitHubTokenStore tokens,
        ILogger logger,
        string host,
        string? expectedLogin,
        string publicKey,
        string profileId,
        string currentEmail,
        string? clientId)
    {
        _api = api;
        _tokens = tokens;
        _logger = logger;
        _host = host;
        _expectedLogin = string.IsNullOrWhiteSpace(expectedLogin) ? null : expectedLogin.Trim();
        _publicKey = publicKey;
        _keyTitle = $"GitPersona · {Environment.MachineName} · {profileId}";
        _currentEmail = currentEmail;
        _clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        SavedLogin = _expectedLogin is not null && SafeReadToken(_expectedLogin) is not null ? _expectedLogin : null;
        RememberToken = true;
        Step = ConnectStep.ChooseMethod;
    }

    public event EventHandler<bool>? CloseRequested;

    [ObservableProperty]
    public partial ConnectStep Step { get; set; }

    [ObservableProperty]
    public partial string? UserCode { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    public partial string? WarningText { get; set; }

    [ObservableProperty]
    public partial bool RememberToken { get; set; }

    [ObservableProperty]
    public partial string? KeyStatus { get; set; }

    [ObservableProperty]
    public partial string? EmailNote { get; set; }

    [ObservableProperty]
    public partial EmailOption? SelectedEmail { get; set; }

    public ObservableCollection<EmailOption> Emails { get; } = [];

    public string? SavedLogin { get; }

    public bool HasSavedLogin => SavedLogin is not null;

    public string SavedLoginText => Loc.Format("Connect_UseSaved", SavedLogin);

    public bool HasClientId => _clientId is not null;

    public string AccountTip => _expectedLogin is null ? Loc["Connect_AccountTipAny"] : Loc.Format("Connect_AccountTip", _expectedLogin);

    public string? MismatchText => _identity is null ? null : Loc.Format("Connect_Mismatch", _identity.User.Login, _expectedLogin);

    public string? UseThisAccountText => _identity is null ? null : Loc.Format("Connect_UseThisAccount", _identity.User.Login);

    public string? SignedInText => _identity is null ? null : Loc.Format("Connect_SignedInAs", _identity.User.Login);

    public ConnectResult? Result { get; private set; }

    private Uri? VerificationUri { get; set; }

    /// <summary>Called by the window when it closes for any reason.</summary>
    public void Abort() => _cancellation.Cancel();

    public void Dispose() => _cancellation.Dispose();

    /// <summary>The token comes from a password box in the window.</summary>
    public async Task SubmitTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        await RunAsync(() => ContinueWithTokenAsync(token.Trim(), fromStore: false));
    }

    [RelayCommand(CanExecute = nameof(HasClientId))]
    private Task SignInWithBrowserAsync() => RunAsync(async () =>
    {
        StatusText = Loc["Connect_Starting"];
        var code = await _api.StartDeviceFlowAsync(_host, _clientId!, _cancellation.Token);
        UserCode = code.UserCode;
        VerificationUri = code.VerificationUri;
        Step = ConnectStep.DeviceCode;
        var token = await _api.WaitForDeviceTokenAsync(_host, _clientId!, code, _cancellation.Token);
        await ContinueWithTokenAsync(token, fromStore: false);
    });

    [RelayCommand]
    private void OpenVerificationPage()
    {
        try
        {
            Clipboard.SetText(UserCode ?? string.Empty);
        }
        catch (COMException)
        {
            // The code is also shown on screen; copying is a convenience.
        }

        OpenInBrowser(VerificationUri);
    }

    [RelayCommand]
    private void UseToken()
    {
        ErrorText = null;
        Step = ConnectStep.Token;
    }

    [RelayCommand]
    private void OpenTokenPage() => OpenInBrowser(GitHubEndpoints.For(_host).NewTokenPage);

    [RelayCommand]
    private Task UseSavedSignInAsync() => RunAsync(async () =>
    {
        if (SavedLogin is null || SafeReadToken(SavedLogin) is not { } token)
        {
            return;
        }

        await ContinueWithTokenAsync(token, fromStore: true);
    });

    [RelayCommand]
    private Task AcceptOtherAccountAsync() => RunAsync(async () =>
    {
        _expectedLogin = _identity!.User.Login;
        await FinishAsync();
    });

    [RelayCommand]
    private void BackToStart()
    {
        _token = null;
        _identity = null;
        ErrorText = null;
        Step = ConnectStep.ChooseMethod;
    }

    [RelayCommand]
    private void Done()
    {
        var summary = string.Join(" ", new[] { KeyStatus, WarningText }.Where(s => !string.IsNullOrEmpty(s)));
        Result = new ConnectResult(_identity!.User.Login, SelectedEmail?.Email, summary);
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Abort();
        CloseRequested?.Invoke(this, false);
    }

    private async Task ContinueWithTokenAsync(string token, bool fromStore)
    {
        Step = ConnectStep.Working;
        StatusText = Loc["Connect_CheckingAccount"];
        try
        {
            _identity = await _api.GetIdentityAsync(_host, token, _cancellation.Token);
        }
        catch (GitHubUnauthorizedException) when (fromStore)
        {
            _tokens.Delete(_host, SavedLogin!);
            throw new UserFacingException(Loc["Connect_SavedExpired"]);
        }
        catch (GitHubUnauthorizedException)
        {
            throw new UserFacingException(Loc["Connect_TokenInvalid"]);
        }

        if (_identity.Scopes is { } scopes && !scopes.Contains("write:public_key") && !scopes.Contains("admin:public_key"))
        {
            throw new UserFacingException(Loc["Connect_MissingScope"]);
        }

        _token = token;
        OnPropertyChanged(nameof(MismatchText));
        OnPropertyChanged(nameof(UseThisAccountText));
        OnPropertyChanged(nameof(SignedInText));
        if (_expectedLogin is not null && !_expectedLogin.Equals(_identity.User.Login, StringComparison.OrdinalIgnoreCase))
        {
            Step = ConnectStep.Mismatch;
            return;
        }

        await FinishAsync();
    }

    private async Task FinishAsync()
    {
        var login = _identity!.User.Login;
        var token = _token!;
        Step = ConnectStep.Working;
        WarningText = null;

        if (RememberToken)
        {
            try
            {
                _tokens.Save(_host, login, token);
            }
            catch (Win32Exception ex)
            {
                _logger.LogWarning(ex, "Could not save the GitHub token for {Login}", login);
                WarningText = Loc.Format("Connect_TokenNotSaved", ex.Message);
            }
        }

        StatusText = Loc["Connect_CheckingKeys"];
        var keys = await _api.GetKeysAsync(_host, token, _cancellation.Token);
        if (keys.Any(key => GitHubApiService.IsSameKey(key.Key, _publicKey)))
        {
            KeyStatus = Loc.Format("Connect_KeyAlreadyPresent", login);
        }
        else
        {
            StatusText = Loc["Connect_Uploading"];
            try
            {
                await _api.AddKeyAsync(_host, token, _keyTitle, _publicKey, _cancellation.Token);
            }
            catch (GitHubKeyInUseException)
            {
                throw new UserFacingException(Loc["Connect_KeyInUse"]);
            }

            KeyStatus = Loc.Format("Connect_KeyUploaded", login);
        }

        StatusText = Loc["Connect_ReadingEmails"];
        var emails = await _api.GetEmailsAsync(_host, token, _cancellation.Token);
        FillEmails(emails, _identity.User);
        Step = ConnectStep.Done;
    }

    private void FillEmails(IReadOnlyList<GitHubEmail>? emails, GitHubUser user)
    {
        Emails.Clear();
        EmailNote = emails is null ? Loc["Connect_EmailsUnavailable"] : null;
        var verified = (emails ?? []).Where(e => e.Verified).OrderByDescending(e => e.Primary).ToList();
        foreach (var email in verified)
        {
            Emails.Add(new EmailOption(email.Email, Loc.Format(email.Primary ? "Connect_EmailPrimary" : "Connect_EmailVerified", email.Email)));
        }

        var noReply = GitHubApiService.NoReplyEmail(user, _host);
        if (!Emails.Any(e => e.Email.Equals(noReply, StringComparison.OrdinalIgnoreCase)))
        {
            Emails.Add(new EmailOption(noReply, Loc.Format("Connect_EmailNoReply", noReply)));
        }

        var current = Emails.FirstOrDefault(e => e.Email.Equals(_currentEmail, StringComparison.OrdinalIgnoreCase));
        if (current is null && !string.IsNullOrWhiteSpace(_currentEmail))
        {
            // Keep the choice explicit: the user's current email stays available but is not preselected.
            Emails.Add(new EmailOption(_currentEmail, Loc.Format("Connect_EmailKeep", _currentEmail)));
            EmailNote = string.Join(" ", new[] { EmailNote, Loc.Format("Connect_EmailNotOnAccount", _currentEmail, user.Login) }.Where(s => s is not null));
        }

        SelectedEmail = current ?? Emails.First();
    }

    private async Task RunAsync(Func<Task> action)
    {
        ErrorText = null;
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // The window was closed.
        }
        catch (UserFacingException ex)
        {
            Fail(ex.Message);
        }
        catch (DeviceFlowException ex)
        {
            Fail(ex.Error == DeviceFlowError.Unknown ? Loc.Format("Connect_Device_Unknown", ex.Message) : Loc[$"Connect_Device_{ex.Error}"]);
        }
        catch (Exception ex) when (ex is GitHubApiException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "GitHub connection failed");
            Fail(Loc.Format("Connect_Failed", ex.Message));
        }
    }

    private void Fail(string message)
    {
        ErrorText = message;
        Step = ConnectStep.ChooseMethod;
    }

    private string? SafeReadToken(string login)
    {
        try
        {
            return _tokens.Read(_host, login);
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the saved GitHub token for {Login}", login);
            return null;
        }
    }

    private static void OpenInBrowser(Uri? uri)
    {
        if (uri is not null)
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    private sealed class UserFacingException(string message) : Exception(message);
}
