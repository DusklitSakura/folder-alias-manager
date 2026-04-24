using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.DependencyInjection;
using WFAM.App.Services;
using WFAM.App.ViewModels;
using WFAM.App.Views;
using WFAM.App.Views.Pages;

namespace WFAM.App;

public partial class App : Application
{
    private readonly IHost _host;

    public static IServiceProvider Services { get; private set; } = default!;

    public App()
    {
        // .NET (Core) 默认不包含 GB2312/GBK 等传统代码页，需要手动注册。
        // 放在最早的静态构造后、 Host 创建之前，避免 Services 构造时调用 Encoding.GetEncoding("gb2312") 报错。
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .ConfigureLogging(b =>
            {
                b.ClearProviders();
                b.AddDebug();
            })
            .Build();

        Services = _host.Services;
    }

    private static void ConfigureServices(IServiceCollection s)
    {
        // —— WPF-UI 基础设施 ——
        s.AddNavigationViewPageProvider();
        s.AddSingleton<ISnackbarService, SnackbarService>();
        s.AddSingleton<IContentDialogService, ContentDialogService>();
        s.AddSingleton<INavigationService, NavigationService>();

        // —— 业务 Services ——
        s.AddSingleton<IDesktopIniService, DesktopIniService>();
        s.AddSingleton<IAutorunInfService, AutorunInfService>();
        s.AddSingleton<IDriveService, DriveService>();
        s.AddSingleton<IIconService, IconService>();
        s.AddSingleton<IShellService, ShellService>();
        s.AddSingleton<IElevationService, ElevationService>();
        s.AddSingleton<IFolderPickerService, FolderPickerService>();
        s.AddSingleton<INotificationService, NotificationService>();
        s.AddSingleton<ISettingsService, SettingsService>();
        s.AddSingleton<IContextMenuService, ContextMenuService>();
        s.AddSingleton<IThemeManager, ThemeManager>();
        s.AddSingleton<IHistoryService, HistoryService>();
        s.AddSingleton<IUpdateService, UpdateService>();
        s.AddSingleton<IUpdatePromptService, UpdatePromptService>();
        // 本地化服务使用全局单例（TranslateExtension 也会访问同一个实例）
        s.AddSingleton<ILocalizationService>(_ => LocalizationService.Instance);

        // —— ViewModels ——
        s.AddSingleton<MainWindowViewModel>();
        s.AddSingleton<FoldersViewModel>();
        s.AddSingleton<UsbDrivesViewModel>();
        s.AddSingleton<SettingsViewModel>();
        s.AddSingleton<AboutViewModel>();
        s.AddSingleton<HistoryViewModel>();
        s.AddTransient<EditFolderViewModel>();

        // —— Views ——
        s.AddSingleton<MainWindow>();
        s.AddTransient<FoldersPage>();
        s.AddTransient<UsbDrivesPage>();
        s.AddTransient<SettingsPage>();
        s.AddTransient<AboutPage>();
        s.AddTransient<HistoryPage>();
        s.AddTransient<EditFolderWindow>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 抑制 WPF-UI 默认模板里的 PressedForeground 绑定警告与 Storyboard trace 噪音
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Critical;
        PresentationTraceSources.AnimationSource.Switch.Level = SourceLevels.Critical;

        await _host.StartAsync();

        // 启动时加载持久化设置并应用（主题 + 语言）
        var settings = Services.GetRequiredService<ISettingsService>();
        settings.Load();
        var loc = Services.GetRequiredService<ILocalizationService>();
        loc.SetLanguage(settings.Current.Language);
        var themeMgr = Services.GetRequiredService<IThemeManager>();
        themeMgr.Apply(ParseThemeMode(settings.Current.Theme));

        // 右键菜单入口：仅弹小编辑窗，不创建主界面
        if (TryParseEditPath(e.Args, out var editPath))
        {
            var dlg = Services.GetRequiredService<EditFolderWindow>();
            dlg.Show();
            await dlg.LoadAsync(editPath);
            return;
        }

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();

        // 兼容旧调用方式：直接传递路径字符串作为参数。
        await TryHandleCommandLineAsync(e.Args);

        // 启动后台检查更新（如果用户启用）。Fire-and-forget，不阻塞 UI。
        if (settings.Current.AutoCheckUpdate)
            _ = CheckUpdateInBackgroundAsync();
    }

    private static async Task CheckUpdateInBackgroundAsync()
    {
        try
        {
            // 让主窗口完成初始渲染再发请求，避免与启动时的 I/O 抢资源。
            await Task.Delay(TimeSpan.FromSeconds(2));

            var updates = Services.GetRequiredService<IUpdateService>();
            var settings = Services.GetRequiredService<ISettingsService>();
            var prompt = Services.GetRequiredService<IUpdatePromptService>();

            var info = await updates.CheckAsync(onlyIfNewer: true);
            if (info is null) return;

            // 用户曾经"忽略"过这个版本号 → 不再打扰
            if (string.Equals(settings.Current.LastSkippedUpdateVersion, info.Version.ToString(),
                    StringComparison.OrdinalIgnoreCase))
                return;

            await Current.Dispatcher.InvokeAsync(async () => await prompt.PromptAsync(info));
        }
        catch
        {
            // 静默失败：自动检查不应该打扰用户
        }
    }

    private static bool TryParseEditPath(string[] args, out string folderPath)
    {
        folderPath = string.Empty;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--edit", StringComparison.OrdinalIgnoreCase)
                && System.IO.Directory.Exists(args[i + 1]))
            {
                folderPath = args[i + 1];
                return true;
            }
        }
        return false;
    }

    private static WFAM.App.Models.ThemeMode ParseThemeMode(string s)
        => Enum.TryParse<WFAM.App.Models.ThemeMode>(s, ignoreCase: true, out var m) ? m : WFAM.App.Models.ThemeMode.System;

    private static async Task TryHandleCommandLineAsync(string[] args)
    {
        var folders = args
            .Where(a => !string.IsNullOrWhiteSpace(a) && System.IO.Directory.Exists(a))
            .ToList();
        if (folders.Count == 0) return;

        var vm = Services.GetRequiredService<FoldersViewModel>();
        await vm.AddFoldersAsync(folders);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logger = Services.GetService<ILoggerFactory>()?.CreateLogger("UnhandledException");
        logger?.LogError(e.Exception, "Unhandled UI exception");
        MessageBox.Show(e.Exception.Message, "未处理异常", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
