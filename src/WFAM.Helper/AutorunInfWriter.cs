using System.Diagnostics;
using System.Text;

namespace WFAM.Helper;

/// <summary>
/// 写入驱动器根目录下的 autorun.inf。
/// 与 <see cref="DesktopIniWriter"/> 不同的是：
///   - 段名为 [autorun]，键为 label / icon；
///   - 自定义图标作为单独的 .ico 文件复制到盘符根，autorun.inf 引用相对路径。
/// </summary>
internal static class AutorunInfWriter
{
    public static bool Write(
        string drivePath,
        string label,
        string? stagedIconPath,
        string iconTargetName,
        string? backgroundImage = null)
    {
        if (!Directory.Exists(drivePath)) return false;

        var iniPath = Path.Combine(drivePath, "autorun.inf");
        var tempPath = Path.Combine(drivePath, "autorun.tmp");

        if (File.Exists(iniPath)) RunAttrib($"-r -h -s \"{iniPath}\"");

        // 1) 复制图标
        string? iconValueForIni = null;
        if (!string.IsNullOrEmpty(stagedIconPath) && File.Exists(stagedIconPath))
        {
            var dstIcon = Path.Combine(drivePath, iconTargetName);
            if (File.Exists(dstIcon)) RunAttrib($"-r -h -s \"{dstIcon}\"");
            File.Copy(stagedIconPath, dstIcon, overwrite: true);
            RunAttrib($"+h +s \"{dstIcon}\"");
            iconValueForIni = $"{iconTargetName},0";
        }

        // 2) 合并并写入 autorun.inf（系统 ANSI 编码）
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

        var output = MergeIniLines(lines, label, iconValueForIni);

        File.WriteAllLines(tempPath, output, encoding);
        if (File.Exists(iniPath)) File.Delete(iniPath);
        File.Move(tempPath, iniPath);

        RunAttrib($"+h +s \"{iniPath}\"");

        // 同步盘符根 desktop.ini 中的 background 段
        WriteDriveBackground(drivePath, backgroundImage);

        return true;
    }

    private static void WriteDriveBackground(string drivePath, string? backgroundImage)
    {
        var deskIni = Path.Combine(drivePath, "desktop.ini");
        var deskTmp = Path.Combine(drivePath, "desktop.tmp");
        if (File.Exists(deskIni)) RunAttrib($"-r -h -s \"{deskIni}\"");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding;
        try { encoding = Encoding.GetEncoding(0); }
        catch { encoding = Encoding.UTF8; }

        var lines = new List<string>();
        if (File.Exists(deskIni))
        {
            try { lines.AddRange(File.ReadAllLines(deskIni, encoding)); }
            catch
            {
                try { lines = File.ReadAllLines(deskIni, Encoding.UTF8).ToList(); encoding = Encoding.UTF8; }
                catch { /* ignore */ }
            }
        }

        var output = DesktopIniWriter.MergeBackground(lines, backgroundImage);

        if (output.Count == 0 || output.All(string.IsNullOrWhiteSpace))
        {
            try { if (File.Exists(deskIni)) File.Delete(deskIni); } catch { /* ignore */ }
            return;
        }

        try
        {
            File.WriteAllLines(deskTmp, output, encoding);
            if (File.Exists(deskIni)) File.Delete(deskIni);
            File.Move(deskTmp, deskIni);
            RunAttrib($"+h +s \"{deskIni}\"");
        }
        catch
        {
            try { if (File.Exists(deskTmp)) File.Delete(deskTmp); } catch { /* ignore */ }
        }
    }

    /// <summary>删除 autorun.inf、(若存在的) 同名图标文件 与 盘符根 desktop.ini。</summary>
    public static bool Restore(string drivePath, string iconTargetName)
    {
        if (!Directory.Exists(drivePath)) return false;
        var iniPath = Path.Combine(drivePath, "autorun.inf");
        var iconPath = Path.Combine(drivePath, iconTargetName);
        var deskIni = Path.Combine(drivePath, "desktop.ini");

        if (File.Exists(iniPath))
        {
            RunAttrib($"-r -h -s \"{iniPath}\"");
            try { File.Delete(iniPath); }
            catch { return false; }
        }
        if (File.Exists(iconPath))
        {
            RunAttrib($"-r -h -s \"{iconPath}\"");
            try { File.Delete(iconPath); }
            catch { return false; }
        }
        if (File.Exists(deskIni))
        {
            RunAttrib($"-r -h -s \"{deskIni}\"");
            try { File.Delete(deskIni); }
            catch { return false; }
        }
        return true;
    }

    private static List<string> MergeIniLines(List<string> input, string label, string? iconValue)
    {
        var output = new List<string>();
        bool inSection = false, sectionFound = false, labelWritten = false, iconWritten = false;

        foreach (var raw in input)
        {
            var trimmed = raw.Trim();
            if (trimmed.Equals("[autorun]", StringComparison.OrdinalIgnoreCase))
            {
                inSection = true; sectionFound = true; output.Add(raw);
            }
            else if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (inSection)
                {
                    if (!labelWritten && !string.IsNullOrEmpty(label))
                    { output.Add($"label={label}"); labelWritten = true; }
                    if (!iconWritten && !string.IsNullOrEmpty(iconValue))
                    { output.Add($"icon={iconValue}"); iconWritten = true; }
                }
                inSection = false; output.Add(raw);
            }
            else if (inSection && trimmed.StartsWith("label=", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(label)) output.Add($"label={label}");
                labelWritten = true;
            }
            else if (inSection && trimmed.StartsWith("icon=", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(iconValue)) output.Add($"icon={iconValue}");
                iconWritten = true;
            }
            else
            {
                output.Add(raw);
            }
        }

        if (!sectionFound) output.Add("[autorun]");

        if (!labelWritten && !string.IsNullOrEmpty(label))
        {
            var idx = output.FindIndex(l => l.Trim().Equals("[autorun]", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) output.Insert(idx + 1, $"label={label}");
        }
        if (!iconWritten && !string.IsNullOrEmpty(iconValue))
        {
            var idx = output.FindIndex(l => l.Trim().Equals("[autorun]", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) output.Insert(idx + 1, $"icon={iconValue}");
        }
        return output;
    }

    private static void RunAttrib(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("attrib", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            p?.WaitForExit(5000);
        }
        catch { /* best effort */ }
    }
}
