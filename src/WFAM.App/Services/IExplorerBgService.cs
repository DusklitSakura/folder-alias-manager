namespace WFAM.App.Services;

/// <summary>
/// Explorer 背景扩展运行控制：启动/停止注入器（WFAM.BgHost.exe），
/// 以及配置开机自启（HKCU\Run，无需管理员）。
/// </summary>
public interface IExplorerBgService
{
    /// <summary>本地是否带有 host + dll 两个文件（功能可用前提）。</summary>
    bool IsAvailable { get; }

    /// <summary>当前 host 是否在跑。</summary>
    bool IsRunning { get; }

    /// <summary>启动 host 并写入 HKCU\Run（开机自启）。已在跑则只补写自启。</summary>
    ExplorerBgEnableResult Enable();

    /// <summary>触发 host 退出并删除 HKCU\Run。</summary>
    ExplorerBgEnableResult Disable();
}

public enum ExplorerBgEnableResult
{
    Ok,
    HostMissing,
    DllMissing,
    LaunchFailed,
    Failed,
}
