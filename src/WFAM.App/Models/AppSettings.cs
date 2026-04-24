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

    /// <summary>是否在启动时自动检查 GitHub Release 更新</summary>
    public bool AutoCheckUpdate { get; set; } = true;

    /// <summary>用户已"忽略"的最新版本号字符串（避免每次启动重复弹通知）</summary>
    public string LastSkippedUpdateVersion { get; set; } = string.Empty;
}
