using System.IO;
using System.Windows;
using GitPersona.App.Localization;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace GitPersona.App.Services;

public enum DialogChoice
{
    Primary,
    Secondary,
    Cancel,
}

/// <summary>Fluent-styled message boxes and the shell's folder/file pickers.</summary>
public sealed class DialogService
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    public async Task ShowAsync(string title, string message)
    {
        var box = Create(title, message);
        box.CloseButtonText = Loc["Common_OK"];
        await box.ShowDialogAsync();
    }

    /// <param name="secondary">Optional middle button (e.g. "Discard").</param>
    public async Task<DialogChoice> AskAsync(
        string title,
        string message,
        string primary,
        string? secondary = null,
        ControlAppearance primaryAppearance = ControlAppearance.Primary)
    {
        var box = Create(title, message);
        box.PrimaryButtonText = primary;
        box.PrimaryButtonAppearance = primaryAppearance;
        box.CloseButtonText = Loc["Common_Cancel"];
        if (secondary is not null)
        {
            box.SecondaryButtonText = secondary;
            box.IsSecondaryButtonEnabled = true;
        }

        return await box.ShowDialogAsync() switch
        {
            MessageBoxResult.Primary => DialogChoice.Primary,
            MessageBoxResult.Secondary => DialogChoice.Secondary,
            _ => DialogChoice.Cancel,
        };
    }

    public string? PickFolder(string? initialFolder = null)
    {
        var dialog = new OpenFolderDialog();
        if (initialFolder is not null && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(ActiveWindow) == true ? dialog.FolderName : null;
    }

    public string? PickFile(string title, string filter, string? initialFolder = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (initialFolder is not null && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(ActiveWindow) == true ? dialog.FileName : null;
    }

    public static Window? ActiveWindow =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    private static MessageBox Create(string title, string message) => new()
    {
        Title = title,
        ShowTitle = true,
        Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
        Owner = ActiveWindow,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
    };
}
