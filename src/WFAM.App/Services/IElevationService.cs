using WFAM.App.Models;

namespace WFAM.App.Services;

/// <summary>
/// 通过 UAC 提权的 Helper 进程批量写入 desktop.ini。
/// </summary>
public interface IElevationService
{
    /// <summary>是否找得到 Helper 可执行文件。</summary>
    bool IsHelperAvailable { get; }

    /// <summary>批量提权写入。返回每个项目的执行结果。</summary>
    Task<IReadOnlyList<WriteResult>> ElevatedBatchWriteAsync(
        IReadOnlyList<ElevatedWriteRequest> items,
        CancellationToken ct = default);

    /// <summary>批量提权写入 autorun.inf（U 盘 / 本地盘根目录）。</summary>
    Task<IReadOnlyList<WriteResult>> ElevatedBatchAutorunAsync(
        IReadOnlyList<ElevatedAutorunRequest> items,
        CancellationToken ct = default);
}

public sealed record ElevatedWriteRequest(
    string FolderPath,
    string Name,
    string Alias,
    string? IconPath,
    int IconIndex,
    string? BackgroundImage = null,
    bool Restore = false);

/// <summary>
/// 提权写 autorun.inf 的请求。
/// <see cref="StagedIconPath"/> 必须位于 %TEMP%（由 App 侧预处理为 .ico），
/// helper 仅负责把它复制到 <see cref="DrivePath"/> 根目录下命名为 <see cref="IconTargetName"/>。
/// <para>
/// <see cref="BackgroundImage"/> 是写入到驱动器根 desktop.ini 的 IconArea_Image 字段（绝对/相对路径字符串），
/// helper 不会复制图像文件，只把它作为字面量写进 desktop.ini。
/// </para>
/// </summary>
public sealed record ElevatedAutorunRequest(
    string DrivePath,
    string Name,
    string Label,
    string? StagedIconPath,
    string IconTargetName,
    string? BackgroundImage = null,
    bool Restore = false);
