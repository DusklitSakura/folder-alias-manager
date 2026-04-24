using WFAM.App.Models;

namespace WFAM.App.Services;

/// <summary>
/// 通过 GitHub Releases API 检查 WFAM 是否有新版本。
/// </summary>
public interface IUpdateService
{
    /// <summary>当前主程序版本（从 Assembly 读取）。</summary>
    Version CurrentVersion { get; }

    /// <summary>项目 GitHub 主页。</summary>
    string RepositoryUrl { get; }

    /// <summary>
    /// 查询最新发行版。如果 <paramref name="onlyIfNewer"/> 为 true，仅当版本严格大于当前版本才返回非空。
    /// 网络异常时返回 null（由调用方决定是否提示）。
    /// </summary>
    Task<UpdateInfo?> CheckAsync(bool onlyIfNewer, CancellationToken ct = default);

    /// <summary>
    /// 下载 release 资产并解压（zip）/ 准备（exe/msi）到本地暂存目录。
    /// 进度回调范围 0..1。返回准备好的暂存目录路径（包含可立即覆盖到安装目录的全部新文件，
    /// 或单个 .exe/.msi 安装包）。
    /// </summary>
    Task<UpdateStaging> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 在新进程中启动 "退出本进程 → 覆盖文件 → 重启" 脚本，然后调用方应立即退出当前进程。
    /// </summary>
    void ApplyAndRestart(UpdateStaging staging);
}

/// <summary>
/// 已下载完成、待应用的更新包描述。
/// </summary>
public sealed record UpdateStaging(
    string StagingDirectory, // 暂存目录
    UpdateStagingKind Kind,
    string PrimaryFilePath); // ZipExtracted: staging dir; Installer: 单个 .exe/.msi 路径

public enum UpdateStagingKind
{
    /// <summary>已解压的 zip：StagingDirectory 内即为新版本文件树，可直接覆盖。</summary>
    ZipExtracted,
    /// <summary>独立安装包（.msi 或自带 installer 的 .exe）：直接启动它。</summary>
    Installer,
}

