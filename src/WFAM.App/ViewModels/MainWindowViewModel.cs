using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WFAM.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "WFAM · 文件夹别名管理器";

    /// <summary>
    /// 当前进程是否以本地管理员（Elevated）身份运行。
    /// 主程序本身不应该以管理员启动；提权操作通过独立的 DesktopIniHelper.exe 完成。
    /// </summary>
    public bool IsRunningAsAdmin { get; } = DetectAdmin();

    /// <summary>
    /// 用户是否已主动关闭管理员警告横幅。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAdminWarning))]
    private bool _isAdminWarningDismissed;

    /// <summary>横幅可见条件：当前以管理员运行 且 用户尚未关闭。</summary>
    public bool ShowAdminWarning => IsRunningAsAdmin && !IsAdminWarningDismissed;

    [RelayCommand]
    private void DismissAdminWarning() => IsAdminWarningDismissed = true;

    private static bool DetectAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
