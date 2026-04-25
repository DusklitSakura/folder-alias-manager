using System.Diagnostics;
using System.IO;
using System.Text;
using WFAM.App.Models;

namespace WFAM.App.Services;

/// <inheritdoc />
public sealed class DesktopIniService : IDesktopIniService
{
    public async Task<DesktopIniInfo> ReadAsync(string folderPath, CancellationToken ct = default)
    {
        var iniPath = Path.Combine(folderPath, "desktop.ini");
        if (!File.Exists(iniPath))
            return new DesktopIniInfo(null, null, 0);

        // 多编码尝试：Windows 中文系统通常是 GBK，但也可能为 UTF-8/16
        string[]? lines = null;
        foreach (var enc in CandidateEncodings())
        {
            try
            {
                lines = await File.ReadAllLinesAsync(iniPath, enc, ct).ConfigureAwait(false);
                break;
            }
            catch (DecoderFallbackException) { /* try next */ }
            catch (IOException) { return new DesktopIniInfo(null, null, 0); }
        }
        if (lines is null) return new DesktopIniInfo(null, null, 0);

        string? alias = null, iconPath = null, background = null;
        var iconIndex = 0;
        var section = string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line;
                continue;
            }

            if (section.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase))
            {
                if (line.StartsWith("LocalizedResourceName=", StringComparison.OrdinalIgnoreCase))
                    alias = line["LocalizedResourceName=".Length..];
                else if (line.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line["IconResource=".Length..];
                    var commaIdx = value.LastIndexOf(',');
                    if (commaIdx > 0)
                    {
                        iconPath = value[..commaIdx];
                        int.TryParse(value[(commaIdx + 1)..], out iconIndex);
                    }
                    else iconPath = value;
                }
            }
            else if (section.Equals(BackgroundSection, StringComparison.OrdinalIgnoreCase))
            {
                if (line.StartsWith("IconArea_Image=", StringComparison.OrdinalIgnoreCase))
                    background = line["IconArea_Image=".Length..];
            }
        }
        return new DesktopIniInfo(alias, iconPath, iconIndex, background);
    }

    public Task<WriteResult> WriteAsync(string folderPath, string alias, string? iconPath, int iconIndex, string? backgroundImage, CancellationToken ct = default)
    {
        return Task.Run(() => WriteCore(folderPath, alias, iconPath, iconIndex, backgroundImage), ct);
    }

    public Task<WriteResult> RestoreAsync(string folderPath, CancellationToken ct = default)
    {
        return Task.Run(() => RestoreCore(folderPath), ct);
    }

    private static WriteResult RestoreCore(string folderPath)
    {
        var name = Path.GetFileName(folderPath);
        if (!Directory.Exists(folderPath))
            return new WriteResult(folderPath, name, WriteOutcome.Failed, "目录不存在");
        var iniPath = Path.Combine(folderPath, "desktop.ini");
        var stagedIco = Path.Combine(folderPath, Helpers.IconStaging.CopiedFileName);
        RunAttrib($"-r \"{folderPath}\"");
        if (File.Exists(iniPath))
        {
            RunAttrib($"-r -h -s \"{iniPath}\"");
            try { File.Delete(iniPath); }
            catch (UnauthorizedAccessException ex) { return new WriteResult(folderPath, name, WriteOutcome.AccessDenied, ex.Message); }
            catch (IOException ex) { return new WriteResult(folderPath, name, WriteOutcome.Failed, ex.Message); }
        }
        if (File.Exists(stagedIco))
        {
            RunAttrib($"-r -h -s -a \"{stagedIco}\"");
            try { File.Delete(stagedIco); }
            catch (UnauthorizedAccessException ex) { return new WriteResult(folderPath, name, WriteOutcome.AccessDenied, ex.Message); }
            catch (IOException ex) { return new WriteResult(folderPath, name, WriteOutcome.Failed, ex.Message); }
        }
        return new WriteResult(folderPath, name, WriteOutcome.Success);
    }

    private static WriteResult WriteCore(string folderPath, string alias, string? iconPath, int iconIndex, string? backgroundImage)
    {
        var name = Path.GetFileName(folderPath);
        if (!Directory.Exists(folderPath))
            return new WriteResult(folderPath, name, WriteOutcome.Failed, "目录不存在");

        var iniPath = Path.Combine(folderPath, "desktop.ini");
        var tempPath = Path.Combine(folderPath, "desktop.tmp");

        // 解除属性以便写入（best-effort）
        RunAttrib($"-r \"{folderPath}\"");
        if (File.Exists(iniPath)) RunAttrib($"-r -h -s \"{iniPath}\"");

        var encoding = SystemAnsiEncoding;
        var lines = new List<string>();
        if (File.Exists(iniPath))
        {
            foreach (var enc in CandidateEncodings())
            {
                try { lines = File.ReadAllLines(iniPath, enc).ToList(); encoding = enc; break; }
                catch { /* try next */ }
            }
        }

        var output = MergeLines(lines, alias, iconPath, iconIndex);
        output = MergeBackground(output, backgroundImage);

        try
        {
            File.WriteAllLines(tempPath, output, encoding);
            if (File.Exists(iniPath)) File.Delete(iniPath);
            File.Move(tempPath, iniPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDelete(tempPath);
            return new WriteResult(folderPath, name, WriteOutcome.AccessDenied, ex.Message);
        }
        catch (IOException ex)
        {
            TryDelete(tempPath);
            return new WriteResult(folderPath, name, WriteOutcome.Failed, ex.Message);
        }

        RunAttrib($"+h +s \"{iniPath}\"");
        RunAttrib($"+r \"{folderPath}\"");
        return new WriteResult(folderPath, name, WriteOutcome.Success);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ignore */ }
    }

    private static List<string> MergeLines(List<string> input, string alias, string? iconPath, int iconIndex)
    {
        var output = new List<string>();
        bool inSection = false, sectionFound = false, aliasWritten = false, iconWritten = false;

        foreach (var raw in input)
        {
            var trimmed = raw.Trim();
            if (trimmed.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase))
            {
                inSection = true; sectionFound = true; output.Add(raw);
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
                inSection = false; output.Add(raw);
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

    private static IEnumerable<Encoding> CandidateEncodings()
    {
        // .NET (Core) 默认不含 GB2312/GBK，需注册代码页提供者
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // 首选系统 ANSI 代码页（zh-CN 上是 CP936 = GBK）以与 Explorer 默认解码一致
        yield return SystemAnsiEncoding;
        // GBK 明确带一下，避免某些环境 ACP 不是 936 但文件却是 GBK 写入
        Encoding gbk;
        try { gbk = Encoding.GetEncoding("GBK"); }
        catch { gbk = Encoding.GetEncoding(936); }
        if (gbk.CodePage != SystemAnsiEncoding.CodePage) yield return gbk;
        yield return Encoding.UTF8;
        yield return Encoding.Unicode;
        yield return Encoding.BigEndianUnicode;
    }

    // ---- 自定义背景（[ExtShellFolderViews] / [{BE098140-...}].IconArea_Image） ----

    private const string ExtShellSection = "[ExtShellFolderViews]";
    private const string BackgroundGuid = "{BE098140-A513-11D0-A3A4-00C04FD706EC}";
    private const string BackgroundSection = "[" + BackgroundGuid + "]";

    /// <summary>
    /// 重新构造 [ExtShellFolderViews] 与 [{BE098140-...}] 两段：
    /// 若 <paramref name="backgroundImage"/> 为空（null/空字符串）则把这两段移除（恢复默认背景）；
    /// 否则把已有段替换为最新内容（IconArea_Image=...）。
    /// </summary>
    internal static List<string> MergeBackground(List<string> input, string? backgroundImage)
    {
        // 先剔除原有的两段
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

        if (string.IsNullOrEmpty(backgroundImage))
            return stripped;

        if (stripped.Count > 0 && !string.IsNullOrWhiteSpace(stripped[^1]))
            stripped.Add(string.Empty);
        stripped.Add(ExtShellSection);
        stripped.Add($"{BackgroundGuid}={BackgroundGuid}");
        stripped.Add(BackgroundSection);
        stripped.Add("Attributes=1");
        stripped.Add($"IconArea_Image={backgroundImage}");
        return stripped;
    }

    private static Encoding SystemAnsiEncoding
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // 0 表示 CP_ACP（系统当前 ANSI 代码页）
            try { return Encoding.GetEncoding(0); }
            catch { return Encoding.UTF8; }
        }
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
            p?.WaitForExit(3000);
        }
        catch { /* best effort */ }
    }
}
