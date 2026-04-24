using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WFAM.App.Helpers;
using WFAM.App.Models;
namespace WFAM.App.Services;

/// <inheritdoc />
public sealed class AutorunInfService : IAutorunInfService
{
    public string DriveIconFileName => "autorun.ico";

    public async Task<AutorunInfInfo> ReadAsync(string drivePath, CancellationToken ct = default)
    {
        var iniPath = Path.Combine(drivePath, "autorun.inf");
        if (!File.Exists(iniPath))
            return new AutorunInfInfo(null, null, 0);

        string[]? lines = null;
        foreach (var enc in CandidateEncodings())
        {
            try
            {
                lines = await File.ReadAllLinesAsync(iniPath, enc, ct).ConfigureAwait(false);
                break;
            }
            catch (DecoderFallbackException) { /* try next */ }
            catch (IOException) { return new AutorunInfInfo(null, null, 0); }
        }
        if (lines is null) return new AutorunInfInfo(null, null, 0);

        string? label = null, iconPath = null;
        var iconIndex = 0;
        var inSection = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Equals("[autorun]", StringComparison.OrdinalIgnoreCase)) inSection = true;
            else if (line.StartsWith('[') && line.EndsWith(']')) inSection = false;
            else if (inSection)
            {
                if (line.StartsWith("label=", StringComparison.OrdinalIgnoreCase))
                    label = line["label=".Length..];
                else if (line.StartsWith("icon=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line["icon=".Length..];
                    var commaIdx = value.LastIndexOf(',');
                    if (commaIdx > 0)
                    {
                        iconPath = value[..commaIdx];
                        int.TryParse(value[(commaIdx + 1)..], out iconIndex);
                    }
                    else iconPath = value;
                }
            }
        }
        return new AutorunInfInfo(label, iconPath, iconIndex);
    }

    public Task<WriteResult> WriteAsync(string drivePath, string label, string? stagedIcoPath, CancellationToken ct = default)
        => Task.Run(() => WriteCore(drivePath, label, stagedIcoPath), ct);

    public Task<WriteResult> RestoreAsync(string drivePath, CancellationToken ct = default)
        => Task.Run(() => RestoreCore(drivePath), ct);

    private WriteResult WriteCore(string drivePath, string label, string? stagedIcoPath)
    {
        var name = DescribeDrive(drivePath);
        if (!Directory.Exists(drivePath))
            return new WriteResult(drivePath, name, WriteOutcome.Failed, "驱动器不存在或未就绪");

        var iniPath = Path.Combine(drivePath, "autorun.inf");
        var tempPath = Path.Combine(drivePath, "autorun.tmp");

        // 解除已有 autorun.inf 的属性以便写入
        if (File.Exists(iniPath)) RunAttrib($"-r -h -s \"{iniPath}\"");

        // 1) 处理图标（可选）
        string? iconValueForIni = null;
        if (!string.IsNullOrEmpty(stagedIcoPath) && File.Exists(stagedIcoPath))
        {
            var dstIcon = Path.Combine(drivePath, DriveIconFileName);
            if (File.Exists(dstIcon)) RunAttrib($"-r -h -s \"{dstIcon}\"");
            try
            {
                File.Copy(stagedIcoPath, dstIcon, overwrite: true);
                RunAttrib($"+h +s \"{dstIcon}\"");
                iconValueForIni = $"{DriveIconFileName},0";
            }
            catch (UnauthorizedAccessException ex)
            {
                return new WriteResult(drivePath, name, WriteOutcome.AccessDenied, ex.Message);
            }
            catch (IOException ex)
            {
                return new WriteResult(drivePath, name, WriteOutcome.Failed, ex.Message);
            }
        }

        // 2) 合并/写入 autorun.inf
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

        var output = MergeLines(lines, label, iconValueForIni);

        try
        {
            File.WriteAllLines(tempPath, output, encoding);
            if (File.Exists(iniPath)) File.Delete(iniPath);
            File.Move(tempPath, iniPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDelete(tempPath);
            return new WriteResult(drivePath, name, WriteOutcome.AccessDenied, ex.Message);
        }
        catch (IOException ex)
        {
            TryDelete(tempPath);
            return new WriteResult(drivePath, name, WriteOutcome.Failed, ex.Message);
        }

        RunAttrib($"+h +s \"{iniPath}\"");
        return new WriteResult(drivePath, name, WriteOutcome.Success);
    }

    private WriteResult RestoreCore(string drivePath)
    {
        var name = DescribeDrive(drivePath);
        if (!Directory.Exists(drivePath))
            return new WriteResult(drivePath, name, WriteOutcome.Failed, "驱动器不存在或未就绪");

        var iniPath = Path.Combine(drivePath, "autorun.inf");
        var iconPath = Path.Combine(drivePath, DriveIconFileName);

        try
        {
            if (File.Exists(iniPath))
            {
                RunAttrib($"-r -h -s \"{iniPath}\"");
                File.Delete(iniPath);
            }
            if (File.Exists(iconPath))
            {
                RunAttrib($"-r -h -s \"{iconPath}\"");
                File.Delete(iconPath);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return new WriteResult(drivePath, name, WriteOutcome.AccessDenied, ex.Message);
        }
        catch (IOException ex)
        {
            return new WriteResult(drivePath, name, WriteOutcome.Failed, ex.Message);
        }
        return new WriteResult(drivePath, name, WriteOutcome.Success);
    }

    public Task<string?> StageIconAsync(string? sourcePath, int index, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sourcePath))
            return Task.FromResult<string?>(null);

        return Task.Run<string?>(() =>
        {
            try
            {
                if (!File.Exists(sourcePath)) return null;

                var pid = Environment.ProcessId;
                var staged = Path.Combine(
                    Path.GetTempPath(),
                    $"wfam_ico_{pid}_{Guid.NewGuid():N}.ico");

                var ext = Path.GetExtension(sourcePath);
                if (string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, staged, overwrite: true);
                    return staged;
                }

                // 从 .exe / .dll 中提取指定索引图标 → 写为 PNG-encoded .ico
                var img = ExtractBitmap(sourcePath, index);
                if (img is null) return null;

                IcoFileWriter.Save(img, staged);
                return staged;
            }
            catch
            {
                return null;
            }
        }, ct);
    }

    // ---- 辅助 ----

    private static BitmapSource? ExtractBitmap(string file, int index)
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        try
        {
            var n = NativeMethods.ExtractIconEx(file, index, large, small, 1);
            if (n <= 0) return null;
            var handle = large[0] != IntPtr.Zero ? large[0] : small[0];
            if (handle == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(
                handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (large[0] != IntPtr.Zero) NativeMethods.DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero && small[0] != large[0]) NativeMethods.DestroyIcon(small[0]);
        }
    }

    private static List<string> MergeLines(List<string> input, string label, string? iconValue)
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

    private static string DescribeDrive(string drivePath)
    {
        try
        {
            var di = new DriveInfo(drivePath);
            return di.IsReady && !string.IsNullOrEmpty(di.VolumeLabel)
                ? $"{di.Name} ({di.VolumeLabel})"
                : di.Name;
        }
        catch
        {
            return drivePath;
        }
    }

    private static IEnumerable<Encoding> CandidateEncodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        yield return SystemAnsiEncoding;
        Encoding gbk;
        try { gbk = Encoding.GetEncoding("GBK"); }
        catch { gbk = Encoding.GetEncoding(936); }
        if (gbk.CodePage != SystemAnsiEncoding.CodePage) yield return gbk;
        yield return Encoding.UTF8;
        yield return Encoding.Unicode;
        yield return Encoding.BigEndianUnicode;
    }

    private static Encoding SystemAnsiEncoding
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try { return Encoding.GetEncoding(0); }
            catch { return Encoding.UTF8; }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
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
