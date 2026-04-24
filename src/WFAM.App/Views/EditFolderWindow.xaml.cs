using Wpf.Ui;
using Wpf.Ui.Controls;
using WFAM.App.ViewModels;

namespace WFAM.App.Views;

public partial class EditFolderWindow : FluentWindow
{
    private readonly EditFolderViewModel _viewModel;

    public EditFolderWindow(EditFolderViewModel viewModel, ISnackbarService snackbar)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        snackbar.SetSnackbarPresenter(SnackbarPresenter);
    }

    public Task LoadAsync(string folderPath) => _viewModel.LoadAsync(folderPath);

    private async void OnSaveClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var ok = await _viewModel.SaveAsync();
        if (ok) Close();
    }

    private void OnCancelClick(object sender, System.Windows.RoutedEventArgs e) => Close();
}
