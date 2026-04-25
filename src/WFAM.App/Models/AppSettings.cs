namespace WFAM.App.Models;

/// <summary>
/// 持久化的应用设置（保存在 %LOCALAPPDATA%\WFAM\settings.json）。
/// </summary>
public sealed class AppSettings
{
    /// <summary>主题模式：System / Light / Dark / HighContrast</summary>
    public string Theme { get; set; } = "System";

    /// <summary>语言代码：zh-CN / en-US</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>是否在导入时自动扫描图标</summary>
    public bool AutoScanIcons { get; set; } = true;

    /// <summary>
    /// 保存图标时是否复制 .ico 文件到目标文件夹（写相对路径）。
    /// 默认开启：即便日后卸载本程序、原始图标文件被删除，文件夹仍保留好看的图标而不会变成空白。
    /// 关闭则在 desktop.ini 中写入图标的绝对路径（依赖原始文件继续存在）。
    /// 当图标源已位于目标文件夹（含子目录）下时，无论开关如何均不复制。
    /// </summary>
    public bool CopyIconToFolder { get; set; } = true;

    /// <summary>是否在启动时自动检查 GitHub Release 更新</summary>
    public bool AutoCheckUpdate { get; set; } = true;

    /// <summary>用户已"忽略"的最新版本号字符串（避免每次启动重复弹通知）</summary>
    public string LastSkippedUpdateVersion { get; set; } = string.Empty;

    /// <summary>
    /// 是否已安装 Explorer 背景扩展（WFAM.ExplorerBg.dll）。
    /// 仅用于 UI 状态记录；实际是否生效以注册表 Browser Helper Objects 为准。
    /// </summary>
    public bool ExplorerBgInstalled { get; set; } = false;
}
