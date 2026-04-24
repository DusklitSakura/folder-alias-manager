using WFAM.App.Models;

namespace WFAM.App.Services;

/// <summary>
/// 读写文件夹根目录下的 desktop.ini。
/// </summary>
public interface IDesktopIniService
{
    Task<DesktopIniInfo> ReadAsync(string folderPath, CancellationToken ct = default);

    /// <summary>
    /// 直接写入。不会拋出权限/IO 异常，而是返回 <see cref="WriteResult"/>：
    /// <list type="bullet">
    /// <item><see cref="WriteOutcome.Success"/></item>
    /// <item><see cref="WriteOutcome.AccessDenied"/> — 需交给 <see cref="IElevationService"/> 提权写入</item>
    /// <item><see cref="WriteOutcome.Failed"/> — 其他 IO 错误（Message 会携带具体原因）</item>
    /// </list>
    /// </summary>
    Task<WriteResult> WriteAsync(string folderPath, string alias, string? iconPath, int iconIndex, CancellationToken ct = default);

    /// <summary>
    /// 恢复默认：删除 desktop.ini 并去掉文件夹的只读属性。
    /// 返回状态与 <see cref="WriteAsync"/> 一致。
    /// </summary>
    Task<WriteResult> RestoreAsync(string folderPath, CancellationToken ct = default);
}
