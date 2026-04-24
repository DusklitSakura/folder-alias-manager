using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using WFAM.App.ViewModels;

namespace WFAM.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigation,
        INavigationViewPageProvider pageProvider,
        ISnackbarService snackbar,
        IContentDialogService dialog)
    {
        DataContext = viewModel;
        InitializeComponent();

        // 把 NavigationView 绑定到 NavigationService，并注入页面提供器（基于 DI）
        navigation.SetNavigationControl(RootNavigation);
        RootNavigation.SetPageProviderService(pageProvider);

        snackbar.SetSnackbarPresenter(SnackbarPresenter);
        dialog.SetDialogHost(DialogHost);

        // WPF-UI 4.x 的 NavigationView 不会在加载时自动导航到首项；
        // 在窗口首次完成加载后显式导航到「文件夹」页，避免主区域空白。
        Loaded += OnFirstLoaded;
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        RootNavigation.Navigate(typeof(Pages.FoldersPage));
    }
}
