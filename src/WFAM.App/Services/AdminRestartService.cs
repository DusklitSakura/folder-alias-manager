using System.Diagnostics;
using System.IO;

namespace WFAM.App.Services;

/// <inheritdoc />
public sealed class AdminRestartService : IAdminRestartService
{
    public bool RestartAsStandardUser()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return false;

            // explorer.exe 以交互式用户身份运行（Medium IL），由它启动子进程可去掉 Administrator 令牌
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{exe}\"",
                UseShellExecute = true,
            };
            using var p = Process.Start(psi);
            return p is not null;
        }
        catch
        {
            return false;
        }
    }
}
