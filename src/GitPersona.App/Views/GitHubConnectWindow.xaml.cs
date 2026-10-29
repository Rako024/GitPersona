using System.ComponentModel;
using System.Windows;
using GitPersona.App.ViewModels;
using Wpf.Ui.Controls;

namespace GitPersona.App.Views;

public partial class GitHubConnectWindow : FluentWindow
{
    private readonly GitHubConnectViewModel _viewModel;
    private bool _closingFromViewModel;

    public GitHubConnectWindow(GitHubConnectViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += (_, accepted) =>
        {
            _closingFromViewModel = true;
            DialogResult = accepted;
        };
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing with the title bar's X must also stop a device flow that is still polling.
        if (!_closingFromViewModel)
        {
            _viewModel.Abort();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private async void OnSubmitTokenClick(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password ?? string.Empty;
        TokenBox.Password = string.Empty;
        await _viewModel.SubmitTokenAsync(token);
    }
}
