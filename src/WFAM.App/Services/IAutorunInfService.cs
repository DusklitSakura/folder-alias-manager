using WFAM.App.Models;

namespace WFAM.App.Services;

/// <summary>
/// 读写驱动器根目录下的 autorun.inf（用于自定义资源管理器中显示的 label / icon）。
/// </summary>
public interface IAutorunInfService
{
    /// <summary>autorun.inf 中引用的图标文件名（写入到盘符根）。</summary>
    string DriveIconFileName { get; }

    Task<AutorunInfInfo> ReadAsync(string drivePath, CancellationToken ct = default);

    /// <summary>
    /// 写入 autorun.inf；若 <paramref name="stagedIcoPath"/> 非空则同时把该 .ico 复制为
    /// <see cref="DriveIconFileName"/> 放到盘符根并加上隐藏/系统属性。
    /// </summary>
    Task<WriteResult> WriteAsync(
        string drivePath,
        string label,
        string? stagedIcoPath,
        CancellationToken ct = default);

    /// <summary>恢复默认：删除 autorun.inf 与（若存在的）图标文件。</summary>
    Task<WriteResult> RestoreAsync(string drivePath, CancellationToken ct = default);

    /// <summary>
    /// 把指定来源（.ico 直接复制；.exe/.dll 通过 ExtractIconEx 取出指定索引再编码为 .ico）
    /// 暂存到 <c>%TEMP%</c> 下。返回临时 .ico 路径，调用方负责后续删除（或交给提权流程清理）。
    /// 当 <paramref name="sourcePath"/> 为空（默认图标）时返回 null。
    /// </summary>
    Task<string?> StageIconAsync(string? sourcePath, int index, CancellationToken ct = default);
}
