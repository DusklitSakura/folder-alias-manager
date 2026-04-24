using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WFAM.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "WFAM · 文件夹别名管理器";

    /// <summary>
    /// 当前进程是否以本地管理员（Elevated）身份运行。
    /// 主程序本身不应该以管理员启动；提权操作通过独立的 DesktopIniHelper.exe 完成。
    /// </summary>
    public bool IsRunningAsAdmin { get; } = DetectAdmin();

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
