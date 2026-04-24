using WFAM.App.Models;

namespace WFAM.App.Services;

/// <summary>
/// 图标提取服务（exe/dll/ico）。
/// </summary>
public interface IIconService
{
    /// <summary>提取目录下若干 exe 中的可选图标，已做去重。</summary>
    Task<IReadOnlyList<IconEntry>> CollectIconsForFolderAsync(string folderPath, int maxIcons = 50, CancellationToken ct = default);

    /// <summary>从指定文件提取一个图标（用于自定义选择 / 系统图标）。</summary>
    IconEntry? ExtractSingle(string filePath, int index);

    /// <summary>遍历单个文件中前若干图标。</summary>
    IEnumerable<IconEntry> ExtractFromFile(string filePath, int max);

    /// <summary>用于 UI 顶部条目的默认文件夹图标。</summary>
    IconEntry GetDefaultFolderIcon();
}
