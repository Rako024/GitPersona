using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GitPersona.App.Localization;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Ssh;
using Microsoft.Extensions.Logging;

namespace GitPersona.App.ViewModels;

public sealed partial class GenerateKeyViewModel : LocalizedObject
{
    private readonly SshKeyService _keys;
    private readonly ILogger _logger;

    public GenerateKeyViewModel(SshKeyService keys, ILogger logger, string defaultPath, string comment)
    {
        _keys = keys;
        _logger = logger;
        KeyPath = defaultPath;
        Comment = comment;
    }

    [ObservableProperty]
    public partial string KeyPath { get; set; }

    [ObservableProperty]
    public partial string Comment { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public SshKeyInfo? Result { get; private set; }

    /// <summary>Passphrases come from the window's password boxes, which are not data-bound.</summary>
    public async Task<bool> GenerateAsync(string passphrase, string confirmation)
    {
        ErrorMessage = null;
        if (passphrase != confirmation)
        {
            ErrorMessage = Loc["KeyGen_Mismatch"];
            return false;
        }

        IsBusy = true;
        try
        {
            Result = await _keys.GenerateAsync(KeyPath, Comment, passphrase);
            return true;
        }
        catch (SshKeyExistsException ex)
        {
            ErrorMessage = Loc.Format("KeyGen_Exists", ex.Path);
        }
        catch (Exception ex) when (ex is SshKeyException or ToolNotFoundException or ProcessRunException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogError(ex, "Key generation failed");
            ErrorMessage = Loc.Format("KeyGen_Failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }
}
