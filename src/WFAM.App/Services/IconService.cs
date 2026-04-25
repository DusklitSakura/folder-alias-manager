using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WFAM.App.Helpers;
using WFAM.App.Models;

namespace WFAM.App.Services;

/// <inheritdoc />
public sealed class IconService : IIconService
{
    private static readonly Lazy<ImageSource> DefaultFolderImage = new(LoadDefaultFolderImage);

    public IconEntry GetDefaultFolderIcon() => new()
    {
        SourcePath = string.Empty,
        Index = -1,
        DisplayName = "默认文件夹图标",
        Image = DefaultFolderImage.Value,
    };

    public IconEntry? ExtractSingle(string filePath, int index)
    {
        if (!File.Exists(filePath)) return null;
        var img = ExtractIconImage(filePath, index);
        if (img is null) return null;
        return new IconEntry
        {
            SourcePath = filePath,
            Index = index,
            DisplayName = $"{Path.GetFileName(filePath)} [{index}]",
            Image = img,
        };
    }

    public IEnumerable<IconEntry> ExtractFromFile(string filePath, int max)
    {
        for (var i = 0; i < max; i++)
        {
            var entry = ExtractSingle(filePath, i);
            if (entry is null)
            {
                if (i > 0) yield break;
                continue;
            }
            yield return entry;
        }
    }

    public Task<IReadOnlyList<IconEntry>> CollectIconsForFolderAsync(string folderPath, int maxIcons = 50, CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<IconEntry>>(() =>
        {
            var result = new List<IconEntry> { GetDefaultFolderIcon() };
            var hashes = new HashSet<string>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1) 优先插入目录下的 .ico （含 staged 的 directory.ico）
            try
            {
                foreach (var ico in Directory.EnumerateFiles(folderPath, "*.ico", SearchOption.AllDirectories))
                {
                    if (ct.IsCancellationRequested) break;
                    if (result.Count >= maxIcons) break;
                    if (!seenPaths.Add(ico)) continue;
                    var entry = ExtractSingle(ico, 0);
                    if (entry is null) continue;
                    var hash = HashImage(entry.Image);
                    if (hash is null || hashes.Add(hash))
                        result.Add(entry);
                }
            }
            catch { /* ignore enumeration failures */ }

            // 2) 再扫 exe / dll 中的前几个图标
            IEnumerable<string> binFiles;
            try
            {
                binFiles = Directory.EnumerateFiles(folderPath, "*.exe", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(folderPath, "*.dll", SearchOption.AllDirectories));
            }
            catch
            {
                return result;
            }

            foreach (var bin in binFiles)
            {
                if (ct.IsCancellationRequested) break;
                if (result.Count >= maxIcons) break;

                foreach (var entry in ExtractFromFile(bin, 3))
                {
                    if (result.Count >= maxIcons) break;
                    var hash = HashImage(entry.Image);
                    if (hash is null || hashes.Add(hash))
                        result.Add(entry);
                }
            }
            return result;
        }, ct);
    }

    // ----- 内部：图标位图提取 -----

    private static ImageSource? ExtractIconImage(string file, int index)
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

    private static string? HashImage(ImageSource? img)
    {
        if (img is not BitmapSource bs) return null;
        try
        {
            var stride = (bs.PixelWidth * bs.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[bs.PixelHeight * stride];
            bs.CopyPixels(pixels, stride, 0);
            // 简易内容指纹：长度 + 抽样字节
            unchecked
            {
                ulong h = 1469598103934665603UL;
                for (int i = 0; i < pixels.Length; i += Math.Max(1, pixels.Length / 256))
                    h = (h ^ pixels[i]) * 1099511628211UL;
                return $"{bs.PixelWidth}x{bs.PixelHeight}:{h:x16}";
            }
        }
        catch { return null; }
    }

    private static ImageSource LoadDefaultFolderImage()
    {
        // Shell32.dll 的索引 3 通常是文件夹图标
        var sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        var img = ExtractIconImage(sys32, 3);
        return img ?? CreatePlaceholder();
    }

    private static ImageSource CreatePlaceholder()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Goldenrod, null, new Rect(0, 0, 32, 32));
        }
        var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
