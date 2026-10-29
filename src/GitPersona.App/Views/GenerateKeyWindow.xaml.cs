using System.Windows;
using GitPersona.App.ViewModels;
using Wpf.Ui.Controls;

namespace GitPersona.App.Views;

public partial class GenerateKeyWindow : FluentWindow
{
    private readonly GenerateKeyViewModel _viewModel;

    public GenerateKeyWindow(GenerateKeyViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.GenerateAsync(Passphrase.Password ?? string.Empty, Confirmation.Password ?? string.Empty))
        {
            DialogResult = true;
        }
    }
}
