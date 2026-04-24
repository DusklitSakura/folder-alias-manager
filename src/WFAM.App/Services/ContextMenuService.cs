using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WFAM.App.Services;

/// <summary>
/// 通过 HKCU\Software\Classes 注册右键菜单：无需管理员权限，仅影响当前用户。
/// 同时注册 Directory（点击文件夹本身）和 Directory\Background（点击文件夹空白处）。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ContextMenuService : IContextMenuService
{
    private const string KeyName = "WFAM";
    private const string DirectoryShell = @"Software\Classes\Directory\shell\" + KeyName;
    private const string DirectoryBgShell = @"Software\Classes\Directory\Background\shell\" + KeyName;

    public bool IsRegistered
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(DirectoryShell);
            return k is not null;
        }
    }

    public void Register(string menuLabel)
    {
        var exe = GetExecutablePath();
        // 传 --edit，主程序会弹出小编辑窗口而不是主界面
        WriteShellEntry(DirectoryShell, menuLabel, exe, "--edit \"%1\"");
        WriteShellEntry(DirectoryBgShell, menuLabel, exe, "--edit \"%V\"");
    }

    public void Unregister()
    {
        TryDelete(DirectoryShell);
        TryDelete(DirectoryBgShell);
    }

    private static void WriteShellEntry(string subPath, string label, string exe, string argTemplate)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subPath, writable: true)
            ?? throw new InvalidOperationException($"无法创建注册表项：{subPath}");
        key.SetValue(string.Empty, label, RegistryValueKind.String);
        key.SetValue("Icon", $"\"{exe}\",0", RegistryValueKind.String);

        using var cmdKey = key.CreateSubKey("command", writable: true)
            ?? throw new InvalidOperationException($"无法创建注册表项：{subPath}\\command");
        cmdKey.SetValue(string.Empty, $"\"{exe}\" {argTemplate}", RegistryValueKind.String);
    }

    private static void TryDelete(string subPath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // ignore
        }
    }

    private static string GetExecutablePath()
    {
        // 在单文件 / 普通发布下都返回真实 .exe 路径
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            return processPath;

        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                     ?? AppContext.BaseDirectory;
        var candidate = Path.Combine(asmDir, "WFAM.exe");
        return File.Exists(candidate) ? candidate : asmDir;
    }
}
