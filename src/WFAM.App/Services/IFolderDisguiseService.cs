using WFAM.App.Models;

namespace WFAM.App.Services;

/// <summary>
/// 文件夹伪装服务：通过往目标文件夹写入只含 CLSID 的 desktop.ini
/// （并把文件夹标记为 +s +r），让 Explorer 把该文件夹呈现为系统命名空间对象。
/// 撤销时只需删除 desktop.ini 并清除 +s +r。
/// </summary>
public interface IFolderDisguiseService
{
    /// <summary>内置的常用伪装目标（回收站、控制面板、我的电脑、字体…）。</summary>
    IReadOnlyList<DisguisePreset> Presets { get; }

    /// <summary>读取目录下 desktop.ini 检测当前伪装状态。</summary>
    DisguiseState Detect(string folderPath);

    /// <summary>把 <paramref name="folderPath"/> 伪装为指定 CLSID 对应的命名空间对象。</summary>
    Task<WriteResult> DisguiseAsync(string folderPath, string clsid, CancellationToken ct = default);

    /// <summary>移除伪装：删除 desktop.ini 并清除 +s +r。</summary>
    Task<WriteResult> RestoreAsync(string folderPath, CancellationToken ct = default);
}
