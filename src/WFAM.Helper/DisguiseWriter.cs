using System.Text;

namespace WFAM.Helper;

/// <summary>
/// 文件夹伪装写入器：往目标文件夹写入仅含 [.ShellClassInfo].CLSID 的 desktop.ini，
/// 并把目录设为 +s +r，让 Explorer 把它呈现为对应的系统命名空间对象。
/// 与 <see cref="DesktopIniWriter"/> 不同，伪装会完全覆盖原 desktop.ini。
/// </summary>
internal static class DisguiseWriter
{
    public static bool Write(string folderPath, string clsid)
    {
        if (!Directory.Exists(folderPath)) return false;
        var iniPath = Path.Combine(folderPath, "desktop.ini");

        RunAttrib($"-r -s \"{folderPath}\"");
        if (File.Exists(iniPath)) RunAttrib($"-r -h -s \"{iniPath}\"");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding;
        try { encoding = Encoding.GetEncoding(0); }
        catch { encoding = Encoding.UTF8; }

        var sb = new StringBuilder();
        sb.AppendLine("[.ShellClassInfo]");
        sb.AppendLine("CLSID=" + clsid);
        File.WriteAllText(iniPath, sb.ToString(), encoding);

        RunAttrib($"+h +s \"{iniPath}\"");
        RunAttrib($"+s +r \"{folderPath}\"");
        return true;
    }

    public static bool Restore(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return false;
        var iniPath = Path.Combine(folderPath, "desktop.ini");
        RunAttrib($"-r -s \"{folderPath}\"");
        if (File.Exists(iniPath))
        {
            RunAttrib($"-r -h -s \"{iniPath}\"");
            try { File.Delete(iniPath); }
            catch { return false; }
        }
        return true;
    }

    private static void RunAttrib(string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "attrib",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch { /* best effort */ }
    }
}
