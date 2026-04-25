using System.Diagnostics;
using System.Text;

namespace WFAM.Helper;

/// <summary>
/// 复用主项目的 desktop.ini 写入逻辑（独立实现，避免循环引用）。
/// </summary>
internal static class DesktopIniWriter
{
    public static bool Write(string folderPath, string alias, string? iconPath, int iconIndex, string? backgroundImage = null)
    {
        if (!Directory.Exists(folderPath)) return false;

        var iniPath = Path.Combine(folderPath, "desktop.ini");
        var tempPath = Path.Combine(folderPath, "desktop.tmp");

        RunAttrib($"-r \"{folderPath}\"");
        if (File.Exists(iniPath))
        {
            RunIcacls(iniPath);
            RunAttrib($"-r -h -s \"{iniPath}\"");
        }
        RunIcacls(folderPath);

        // 与 Explorer 一致的系统 ANSI 代码页（zh-CN 上为 CP936 = GBK）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding;
        try { encoding = Encoding.GetEncoding(0); }
        catch { encoding = Encoding.UTF8; }

        var lines = new List<string>();
        if (File.Exists(iniPath))
        {
            try { lines.AddRange(File.ReadAllLines(iniPath, encoding)); }
            catch
            {
                try { lines = File.ReadAllLines(iniPath, Encoding.UTF8).ToList(); encoding = Encoding.UTF8; }
                catch { /* ignore */ }
            }
        }

        var output = MergeIniLines(lines, alias, iconPath, iconIndex);
        output = MergeBackground(output, backgroundImage);

        File.WriteAllLines(tempPath, output, encoding);
        if (File.Exists(iniPath)) File.Delete(iniPath);
        File.Move(tempPath, iniPath);

        RunAttrib($"+h +s \"{iniPath}\"");
        RunAttrib($"+r \"{folderPath}\"");

        return true;
    }

    /// <summary>删除 desktop.ini、staged 的 directory.ico 并清除文件夹只读属性。</summary>
    public static bool Restore(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return false;
        var iniPath = Path.Combine(folderPath, "desktop.ini");
        var stagedIco = Path.Combine(folderPath, "directory.ico");
        RunAttrib($"-r \"{folderPath}\"");
        if (File.Exists(iniPath))
        {
            RunIcacls(iniPath);
            RunAttrib($"-r -h -s \"{iniPath}\"");
            try { File.Delete(iniPath); }
            catch { return false; }
        }
        if (File.Exists(stagedIco))
        {
            RunAttrib($"-r -h -s -a \"{stagedIco}\"");
            try { File.Delete(stagedIco); }
            catch { return false; }
        }
        return true;
    }

    private static List<string> MergeIniLines(List<string> input, string alias, string? iconPath, int iconIndex)
    {
        var output = new List<string>();
        bool inSection = false, sectionFound = false, aliasWritten = false, iconWritten = false;

        foreach (var raw in input)
        {
            var trimmed = raw.Trim();
            if (trimmed.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                sectionFound = true;
                output.Add(raw);
            }
            else if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (inSection)
                {
                    if (!aliasWritten && !string.IsNullOrEmpty(alias))
                    { output.Add($"LocalizedResourceName={alias}"); aliasWritten = true; }
                    if (!iconWritten && !string.IsNullOrEmpty(iconPath))
                    { output.Add($"IconResource={iconPath},{iconIndex}"); iconWritten = true; }
                }
                inSection = false;
                output.Add(raw);
            }
            else if (inSection && trimmed.StartsWith("LocalizedResourceName=", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(alias)) output.Add($"LocalizedResourceName={alias}");
                aliasWritten = true;
            }
            else if (inSection && trimmed.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(iconPath)) output.Add($"IconResource={iconPath},{iconIndex}");
                iconWritten = true;
            }
            else
            {
                output.Add(raw);
            }
        }

        if (!sectionFound) output.Add("[.ShellClassInfo]");

        if (!aliasWritten && !string.IsNullOrEmpty(alias))
        {
            var idx = output.FindIndex(l => l.Trim().Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) output.Insert(idx + 1, $"LocalizedResourceName={alias}");
        }
        if (!iconWritten && !string.IsNullOrEmpty(iconPath))
        {
            var idx = output.FindIndex(l => l.Trim().Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) output.Insert(idx + 1, $"IconResource={iconPath},{iconIndex}");
        }
        return output;
    }

    private static void RunAttrib(string args) => RunSilent("attrib", args);

    // 历史实现使用 *S-1-1-0:F (Everyone Full Control)，会把任意目标暴露给所有本地用户，
    // 等同于本地权限提升风险。现降级为 BUILTIN\Users:Modify (S-1-5-32-545)，
    // 仅给当前机器交互式用户写权限，确保后续主程序在非提权状态下仍能更新别名。
    private static void RunIcacls(string path) => RunSilent("icacls", $"\"{path}\" /grant *S-1-5-32-545:M");

    // ---- 自定义背景（[ExtShellFolderViews] / [{BE098140-...}].IconArea_Image） ----

    private const string ExtShellSection = "[ExtShellFolderViews]";
    private const string BackgroundGuid = "{BE098140-A513-11D0-A3A4-00C04FD706EC}";
    private const string BackgroundSection = "[" + BackgroundGuid + "]";

    public static List<string> MergeBackground(List<string> input, string? backgroundImage)
    {
        var stripped = new List<string>(input.Count);
        var section = string.Empty;
        foreach (var raw in input)
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed;
                if (section.Equals(ExtShellSection, StringComparison.OrdinalIgnoreCase) ||
                    section.Equals(BackgroundSection, StringComparison.OrdinalIgnoreCase))
                    continue;
                stripped.Add(raw);
                continue;
            }
            if (section.Equals(ExtShellSection, StringComparison.OrdinalIgnoreCase) ||
                section.Equals(BackgroundSection, StringComparison.OrdinalIgnoreCase))
                continue;
            stripped.Add(raw);
        }
        if (string.IsNullOrEmpty(backgroundImage)) return stripped;
        if (stripped.Count > 0 && !string.IsNullOrWhiteSpace(stripped[^1])) stripped.Add(string.Empty);
        stripped.Add(ExtShellSection);
        stripped.Add($"{BackgroundGuid}={BackgroundGuid}");
        stripped.Add(BackgroundSection);
        stripped.Add("Attributes=1");
        stripped.Add($"IconArea_Image={backgroundImage}");
        return stripped;
    }

    private static void RunSilent(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch { /* best effort */ }
    }
}
