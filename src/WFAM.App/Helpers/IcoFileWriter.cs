using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WFAM.App.Helpers;

/// <summary>
/// 将 <see cref="BitmapSource"/> 写入 .ico 文件。
/// 采用经典 BMP/DIB 编码（XOR 32bpp + AND 1bpp mask）：对所有 Windows 版本与第三方
/// 资源管理器都兼容；PNG-encoded 条目仅对 256×256 广泛支持，对 32 / 48 等小图标
/// 会被部分组件（含 Explorer 的某些刷新路径）判定为损坏。
/// </summary>
internal static class IcoFileWriter
{
    public static void Save(BitmapSource source, string outputPath)
    {
        using var fs = File.Create(outputPath);
        Save(source, fs);
    }

    public static void Save(BitmapSource source, Stream output)
    {
        // 统一为 Bgra32
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;

        // .ico 单帧最大 256；超过则等比缩放
        if (width > 256 || height > 256)
        {
            double scale = 256.0 / System.Math.Max(width, height);
            var scaled = new TransformedBitmap(bgra, new ScaleTransform(scale, scale));
            bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            width = bgra.PixelWidth;
            height = bgra.PixelHeight;
        }

        int xorStride = width * 4;
        var xorTopDown = new byte[xorStride * height];
        bgra.CopyPixels(xorTopDown, xorStride, 0);

        // BMP 在 ICO 内自下而上存放
        var xorBottomUp = FlipRows(xorTopDown, xorStride, height);

        // AND mask：1bpp，按 4 字节对齐；alpha=0 → bit=1（透明）
        int andRowBytes = ((width + 31) / 32) * 4;
        var andMask = BuildAndMask(xorTopDown, width, height, andRowBytes);

        using var bw = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);

        // ICONDIR (6)
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)1);

        // ICONDIRENTRY (16)
        const int bmpHeaderSize = 40;
        int imageSize = bmpHeaderSize + xorBottomUp.Length + andMask.Length;
        bw.Write((byte)(width >= 256 ? 0 : width));
        bw.Write((byte)(height >= 256 ? 0 : height));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write((uint)imageSize);
        bw.Write((uint)(6 + 16));

        // BITMAPINFOHEADER —— biHeight = 高 × 2（XOR + AND）
        bw.Write((uint)bmpHeaderSize);
        bw.Write((int)width);
        bw.Write((int)(height * 2));
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write((uint)0);
        bw.Write((uint)(xorBottomUp.Length + andMask.Length));
        bw.Write((int)0);
        bw.Write((int)0);
        bw.Write((uint)0);
        bw.Write((uint)0);

        bw.Write(xorBottomUp);
        bw.Write(andMask);
    }

    private static byte[] FlipRows(byte[] src, int stride, int height)
    {
        var dst = new byte[src.Length];
        for (int y = 0; y < height; y++)
            System.Buffer.BlockCopy(src, y * stride, dst, (height - 1 - y) * stride, stride);
        return dst;
    }

    private static byte[] BuildAndMask(byte[] xorTopDown, int width, int height, int andRowBytes)
    {
        var mask = new byte[andRowBytes * height];
        int srcStride = width * 4;
        for (int y = 0; y < height; y++)
        {
            int srcY = height - 1 - y;
            for (int x = 0; x < width; x++)
            {
                byte alpha = xorTopDown[srcY * srcStride + x * 4 + 3];
                if (alpha == 0)
                {
                    int byteIdx = y * andRowBytes + (x >> 3);
                    int bit = 7 - (x & 7);
                    mask[byteIdx] |= (byte)(1 << bit);
                }
            }
        }
        return mask;
    }
}
