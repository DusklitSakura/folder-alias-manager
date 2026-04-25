namespace WFAM.App.Services;

/// <summary>
/// 当主程序被以管理员身份启动时，提供「降权重启」能力：
/// 通过 explorer.exe（Medium IL）拉起一个新的 WFAM 实例，从而抛弃高完整性令牌。
/// </summary>
public interface IAdminRestartService
{
    /// <summary>
    /// 以普通用户权限重启当前可执行文件。返回 true 表示新进程已成功创建，
    /// 调用方应立即结束当前进程。
    /// </summary>
    bool RestartAsStandardUser();
}
