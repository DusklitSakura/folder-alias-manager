using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WFAM.App.Models;

/// <summary>
/// 一个可选图标条目（来自 exe/dll 中某个索引）。
/// </summary>
public sealed class IconEntry
{
    /// <summary>图标来源文件路径，空字符串表示 Windows 默认文件夹图标。</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>在该文件中的图标索引；-1 表示默认。</summary>
    public int Index { get; init; } = -1;

    /// <summary>用于在 UI 中预览的图像（可绑定到 Image.Source）。</summary>
    public ImageSource? Image { get; init; }

    /// <summary>显示名称（例如 "notepad.exe [0]"）。</summary>
    public string DisplayName { get; init; } = string.Empty;

    public bool IsDefault => string.IsNullOrEmpty(SourcePath);
}
