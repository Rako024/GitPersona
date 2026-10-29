using GitPersona.App.ViewModels;
using Wpf.Ui.Controls;

namespace GitPersona.App.Views;

public partial class DiffPreviewWindow : FluentWindow
{
    public DiffPreviewWindow(DiffPreviewViewModel viewModel)
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, applied) => DialogResult = applied;
        InitializeComponent();
    }
}
