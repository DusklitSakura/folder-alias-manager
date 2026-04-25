using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WFAM.App.Helpers;

/// <summary>
/// 写入 desktop.ini 之前对图标路径做一次"是否复制到文件夹下"的归一化处理。
/// </summary>
internal static class IconStaging
{
    /// <summary>复制到目标文件夹时使用的固定文件名。</summary>
    public const string CopiedFileName = "directory.ico";

    /// <summary>
    /// 根据设置返回应写入 desktop.ini 的 (iconPath, iconIndex)。
    /// </summary>
    /// <remarks>
    /// 规则：
    /// <list type="bullet">
    /// <item>iconPath 为空 → 返回 (null, 0)。</item>
    /// <item>iconPath 已位于 folderPath（含子目录）下 → 原样返回（典型场景：目录下的 exe / dll / 已存在的 ico）。</item>
    /// <item>未启用复制 → 原样返回（绝对路径写入）。</item>
    /// <item>启用复制且扩展名为 .ico → 复制到 <c>&lt;folderPath&gt;\directory.ico</c>，返回 (该路径, 0)。</item>
    /// <item>启用复制且来源是 .exe / .dll → 提取指定 index 的图标位图，编码为 .ico 写到 <c>&lt;folderPath&gt;\directory.ico</c>，返回 (该路径, 0)。</item>
    /// <item>任何 IO/提取失败 → 回退到原始 (iconPath, iconIndex)。</item>
    /// </list>
    /// </remarks>
    public static (string? IconPath, int IconIndex) ResolveIconPath(
        string folderPath, string? iconPath, int iconIndex, bool copyEnabled)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return (null, 0);
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return (iconPath, iconIndex);

        if (IsInsideFolder(folderPath, iconPath))
            return (iconPath, iconIndex);

        if (!copyEnabled) return (iconPath, iconIndex);

        var ext = Path.GetExtension(iconPath);
        var dst = Path.Combine(folderPath, CopiedFileName);

        try
        {
            if (string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(iconPath)) return (iconPath, iconIndex);
                {
                    if (File.Exists(dst)) RunAttrib($"-r -h -s -a \"{dst}\"");
                    File.Copy(iconPath, dst, overwrite: true);
                }
                ApplySystemAttributes(dst);
                return (dst, 0);
            }

            // 其它类型 (.exe / .dll / .icl 等)：提取索引图标后写为 .ico
            if (!File.Exists(iconPath)) return (iconPath, iconIndex);
            var bitmap = ExtractIconBitmap(iconPath, iconIndex);
            if (bitmap is null) return (iconPath, iconIndex);
            if (File.Exists(dst)) RunAttrib($"-r -h -s -a \"{dst}\"");
            IcoFileWriter.Save(bitmap, dst);
            ApplySystemAttributes(dst);
            return (dst, 0);
        }
        catch (UnauthorizedAccessException) { return (iconPath, iconIndex); }
        catch (IOException) { return (iconPath, iconIndex); }
    }

    /// <summary>给生成的 directory.ico 打上 +s +h +r +a 属性。</summary>
    private static void ApplySystemAttributes(string path)
    {
        // 用 attrib 一次设置 system / hidden / readonly / archive
        RunAttrib($"+s +h +r +a \"{path}\"");
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

    private static BitmapSource? ExtractIconBitmap(string file, int index)
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

    private static bool IsInsideFolder(string folderPath, string filePath)
    {
        try
        {
            var folderFull = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
            var fileFull = Path.GetFullPath(filePath);
            return fileFull.StartsWith(folderFull, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool FilesEqual(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            using var sa = fa.OpenRead();
            using var sb = fb.OpenRead();
            const int bufSize = 64 * 1024;
            var bufA = new byte[bufSize];
            var bufB = new byte[bufSize];
            int n;
            while ((n = sa.Read(bufA, 0, bufSize)) > 0)
            {
                var m = sb.Read(bufB, 0, bufSize);
                if (n != m) return false;
                if (!bufA.AsSpan(0, n).SequenceEqual(bufB.AsSpan(0, n))) return false;
            }
            return true;
        }
        catch { return false; }
    }
}
