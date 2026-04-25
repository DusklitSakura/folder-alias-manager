using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using WFAM.App.Services;
using WFAM.App.ViewModels;

namespace WFAM.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IContentDialogService _dialog;

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigation,
        INavigationViewPageProvider pageProvider,
        ISnackbarService snackbar,
        IContentDialogService dialog)
    {
        DataContext = _viewModel = viewModel;
        _dialog = dialog;
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

    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        RootNavigation.Navigate(typeof(Pages.FoldersPage));

        // 检测到管理员权限：弹询问对话框，确认则降权重启
        if (_viewModel.IsRunningAsAdmin)
            await PromptElevatedAsync();
    }

    private async System.Threading.Tasks.Task PromptElevatedAsync()
    {
        var loc = App.Services.GetRequiredService<ILocalizationService>();
        var restart = App.Services.GetRequiredService<IAdminRestartService>();
        var notify = App.Services.GetRequiredService<INotificationService>();

        var dialog = new ContentDialog(DialogHost)
        {
            Title = loc["Admin.Dialog.Title"],
            Content = loc["Admin.Dialog.Content"],
            PrimaryButtonText = loc["Admin.Dialog.RestartAsUser"],
            CloseButtonText = loc["Admin.Dialog.KeepRunning"],
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            if (restart.RestartAsStandardUser())
            {
                Application.Current.Shutdown();
                return;
            }
            // 重启失败：保留警告横幅并提示
            notify.Warning(loc["Common.Failed"], loc["Admin.Restart.Failed"]);
            _viewModel.IsAdminWarningDismissed = false;
        }
        else
        {
            // 用户选择继续以管理员运行 → 显示警告横幅
            _viewModel.IsAdminWarningDismissed = false;
        }
    }
}
