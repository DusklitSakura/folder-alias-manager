using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace WFAM.App.Services;

/// <summary>
/// 管理 WFAM.BgHost.exe 进程与 HKCU\Run 自启项。
///
/// host 文件名固定 "WFAM.BgHost.exe"，DLL 固定 "WFAM.ExplorerBg.dll"，
/// 都必须与 WFAM.App.exe 同目录。
/// </summary>
public sealed class ExplorerBgService : IExplorerBgService
{
    private const string HostFileName = "WFAM.BgHost.exe";
    private const string DllFileName  = "WFAM.ExplorerBg.dll";
    private const string RunKeyPath   = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "WFAM.BgHost";

    private static string AppDir =>
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    private static string HostPath => Path.Combine(AppDir, HostFileName);
    private static string DllPath  => Path.Combine(AppDir, DllFileName);

    public bool IsAvailable => File.Exists(HostPath) && File.Exists(DllPath);

    public bool IsRunning =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(HostFileName)).Length > 0;

    public ExplorerBgEnableResult Enable()
    {
        if (!File.Exists(HostPath)) return ExplorerBgEnableResult.HostMissing;
        if (!File.Exists(DllPath))  return ExplorerBgEnableResult.DllMissing;

        if (!IsRunning)
        {
            try
            {
                var psi = new ProcessStartInfo(HostPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDir,
                };
                Process.Start(psi);
            }
            catch
            {
                return ExplorerBgEnableResult.LaunchFailed;
            }
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(RunValueName, $"\"{HostPath}\"", RegistryValueKind.String);
        }
        catch { return ExplorerBgEnableResult.Failed; }

        return ExplorerBgEnableResult.Ok;
    }

    public ExplorerBgEnableResult Disable()
    {
        // 1. 触发退出
        if (File.Exists(HostPath) && IsRunning)
        {
            try
            {
                var psi = new ProcessStartInfo(HostPath, "--stop")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDir,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5_000);
            }
            catch { /* 即使失败也继续删自启 */ }
        }

        // 2. 删自启
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch { return ExplorerBgEnableResult.Failed; }

        return ExplorerBgEnableResult.Ok;
    }
}
