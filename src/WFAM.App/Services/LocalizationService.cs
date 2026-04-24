using System.ComponentModel;

namespace WFAM.App.Services;

public sealed class LocalizationService : ILocalizationService
{
    public static LocalizationService Instance { get; } = new();

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new()
    {
        ["zh-CN"] = ZhCn(),
        ["en-US"] = EnUs(),
    };

    private string _current = "zh-CN";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguage => _current;

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = new[]
    {
        new LanguageOption("zh-CN", "简体中文"),
        new LanguageOption("en-US", "English"),
    };

    public void SetLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !_tables.ContainsKey(code) || code == _current)
            return;
        _current = code;
        // 通知所有绑定 ([key]) 重新求值
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
    }

    public string this[string key]
    {
        get
        {
            if (_tables.TryGetValue(_current, out var t) && t.TryGetValue(key, out var v))
                return v;
            if (_tables["zh-CN"].TryGetValue(key, out var fallback))
                return fallback;
            return key;
        }
    }

    private static Dictionary<string, string> ZhCn() => new()
    {
        // 应用 / 标题栏
        ["App.Title"] = "WFAM · Windows 文件夹别名管理器",

        // 管理员警告
        ["Admin.Warning.Title"] = "检测到以管理员身份运行",
        ["Admin.Warning.Subtitle"] = "本程序不应该以管理员权限启动。需要提权的写入会自动调用独立的 DesktopIniHelper.exe 并弹出 UAC；以管理员启动主程序会导致拖放不可用（资源管理器与高完整性进程不互通）并增加不必要的风险。请关闭后以普通用户身份重新启动。",

        // 导航
        ["Nav.Folders"] = "文件夹",
        ["Nav.Drives"] = "U盘 / 驱动器",
        ["Nav.Settings"] = "设置",
        ["Nav.About"] = "关于",
        ["Nav.History"] = "历史记录",

        // 文件夹页
        ["Folders.Title"] = "文件夹",
        ["Folders.Heading"] = "文件夹别名 & 图标",
        ["Folders.Description"] = "拖放文件夹到下方区域，或点击「导入」选择。修改别名/图标后保存即可。",
        ["Folders.DropPrimary"] = "拖放文件夹到此处",
        ["Folders.DropSecondary"] = "支持多选；自动读取已存在的别名/图标",
        ["Folders.Import"] = "导入文件夹",
        ["Folders.AutoScanIcons"] = "自动扫描图标",
        ["Folders.Empty.Title"] = "还没有文件夹",
        ["Folders.Empty.Description"] = "导入或拖放文件夹以开始管理它们的别名",
        ["Folders.AliasPlaceholder"] = "输入文件夹别名…",
        ["Folders.Tip.PickIcon"] = "选择自定义图标",
        ["Folders.Tip.OpenInExplorer"] = "在资源管理器中打开",
        ["Folders.Tip.Remove"] = "移除",
        ["Folders.Tip.RestoreDefault"] = "恢复默认别名与图标",
        ["Folders.Restoring"] = "正在恢复默认…",
        ["Folders.Clear"] = "清空",
        ["Folders.RefreshIcons"] = "刷新图标",
        ["Folders.Save"] = "保存更改",

        // U盘 / 驱动器页
        ["Drives.Title"] = "U盘 / 驱动器",
        ["Drives.Heading"] = "自定义 autorun.inf · 显示名 & 图标",
        ["Drives.Description"] = "为可移动介质（U 盘 / 移动硬盘）写入 autorun.inf，自定义资源管理器中显示的名称与图标。图标会以 .ico 文件复制到盘符根，便于换台机器仍生效。",
        ["Drives.Hint.Title"] = "为什么不自动扫描驱动器内图标？",
        ["Drives.Hint.Subtitle"] = "扫描可移动介质（U 盘 / 移动硬盘）会递归读取其中的 .exe / .dll 以提取图标，容量较大时会占用大量 I/O 并导致 UI 长时间无响应。请点击右侧「选择自定义图标」手动指定 .ico / .exe / .dll。",
        ["Drives.Refresh"] = "重新扫描",
        ["Drives.IncludeFixed"] = "同时显示本地盘",
        ["Drives.LabelPlaceholder"] = "输入驱动器显示名…",
        ["Drives.Empty.Title"] = "未发现可配置的驱动器",
        ["Drives.Empty.Description"] = "插入可移动介质或点击「重新扫描」",
        ["Drives.Tip.PickIcon"] = "选择自定义图标（.ico/.exe/.dll）",
        ["Drives.Tip.RestoreDefault"] = "恢复默认（删除 autorun.inf）",
        ["Drives.Tip.OpenInExplorer"] = "在资源管理器中打开",
        ["Drives.Save"] = "保存到驱动器",
        ["Drives.Busy.Scanning"] = "正在扫描驱动器…",
        ["Drives.Busy.ScanningIcons"] = "扫描 {0} 内图标…",
        ["Drives.Busy.Saving"] = "正在写入 autorun.inf…",
        ["Drives.Busy.Restoring"] = "正在恢复默认…",

        // 驱动器类型
        ["DriveType.Removable"] = "可移动介质",
        ["DriveType.Fixed"] = "本地硬盘",
        ["DriveType.Network"] = "网络驱动器",
        ["DriveType.CDRom"] = "光驱",
        ["DriveType.Ram"] = "内存盘",
        ["DriveType.NoRootDirectory"] = "未挂载",
        ["DriveType.Unknown"] = "未知类型",

        // 设置页
        ["Settings.Title"] = "设置",
        ["Settings.Group.Appearance"] = "外观",
        ["Settings.Theme"] = "主题",
        ["Settings.Theme.Description"] = "切换浅色 / 深色 / 高对比度主题，或跟随系统。",
        ["Settings.Theme.System"] = "跟随系统",
        ["Settings.Theme.Light"] = "浅色",
        ["Settings.Theme.Dark"] = "深色",
        ["Settings.Theme.HighContrast"] = "高对比度",
        ["Settings.Group.Language"] = "语言",
        ["Settings.Language"] = "界面语言",
        ["Settings.Language.Description"] = "切换应用界面语言（立即生效）。",
        ["Settings.Group.Integration"] = "系统集成",
        ["Settings.ContextMenu"] = "文件夹右键菜单",
        ["Settings.ContextMenu.Description"] = "在 Windows 文件夹的右键菜单中加入「使用 WFAM 管理别名」入口。",
        ["Settings.ContextMenu.On"] = "已注册到右键菜单",
        ["Settings.ContextMenu.Off"] = "未注册",
        ["Settings.ContextMenu.Register"] = "注册",
        ["Settings.ContextMenu.Unregister"] = "取消注册",
        ["Settings.ContextMenu.MenuLabel"] = "使用 WFAM 管理别名",

        // 更新
        ["Settings.Group.Update"] = "更新",
        ["Settings.Update"] = "检查更新",
        ["Settings.Update.Description"] = "自动从仓库获取最新发行版。",
        ["Settings.Update.Auto"] = "启动时自动检查更新",
        ["Settings.Update.CurrentVersion"] = "当前版本：",
        ["Update.CheckNow"] = "立即检查",
        ["Update.Checking"] = "正在检查更新…",
        ["Update.UpToDate"] = "已是最新版本。",
        ["Update.Available"] = "发现新版本",
        ["Update.Available.Format"] = "发现新版本 {0}，点击「打开发布页」下载。",
        ["Update.OpenRelease"] = "打开发布页",
        ["Update.UpdateNow"] = "立即更新",
        ["Update.SkipThisVersion"] = "忽略此版本",
        ["Update.Skipped"] = "已忽略此版本。",
        ["Update.CheckFailed"] = "检查更新失败（请检查网络）。",
        ["Update.OpenRepository"] = "打开项目主页",
        ["Update.Notify.NewVersion"] = "检测到新版本 {0}，可在「设置」页查看详情。",

        // 更新对话框
        ["Update.Dialog.Title"] = "检测到新版本 {0}",
        ["Update.Dialog.Subtitle"] = "发现 WFAM 新版本 {0}（当前 {1}）。选择「立即更新」后应用会下载并自动重启。",
        ["Update.Dialog.ReleaseNotes"] = "更新内容：",
        ["Update.Dialog.NoNotes"] = "（本次发布未提供发行说明）",
        ["Update.Dialog.UpdateNow"] = "立即更新",
        ["Update.Dialog.Later"] = "稍后更新",
        ["Update.Dialog.Skip"] = "忽略此版本",
        ["Update.Dialog.Downloading.Title"] = "正在下载更新",
        ["Update.Downloading"] = "下载中… {0}%",
        ["Update.Failed"] = "更新失败",
        ["Update.NoAsset"] = "此 Release 未提供可下载的 .zip / .exe / .msi 资产。",

        // 关于页
        ["About.Title"] = "关于",
        ["About.AppName"] = "WFAM · 文件夹别名管理器",
        ["About.VersionFormat"] = "版本",
        ["About.Description"] =
            "通过现代化 WPF 界面，批量配置 Windows 文件夹的本地化别名（LocalizedResourceName）" +
            "及自定义图标（IconResource）。基于 .NET 10、WPF-UI 与 MVVM 架构。",
        ["About.Tech"] = "技术栈",
        ["About.Tech.Body"] = ".NET 10 · WPF · WPF-UI · CommunityToolkit.Mvvm · Microsoft.Extensions.*",
        ["About.OpenWpfUi"] = "WPF-UI 项目主页",

        // 通用
        ["Common.Yes"] = "是",
        ["Common.No"] = "否",
        ["Common.Cancel"] = "取消",
        ["Common.Success"] = "成功",
        ["Common.Failed"] = "失败",
        ["Notify.ContextMenu.Registered"] = "已添加到资源管理器右键菜单。",
        ["Notify.ContextMenu.Unregistered"] = "已从右键菜单移除。",
        ["Notify.ContextMenu.Failed"] = "操作右键菜单失败：{0}",

        // 编辑窗口
        ["Edit.Title"] = "编辑文件夹别名",
        ["Edit.Alias"] = "别名",
        ["Edit.Icon"] = "图标",
        ["Edit.Loading"] = "正在读取…",
        ["Edit.Saving"] = "正在保存…",
        ["Edit.WaitingUac"] = "等待 UAC 授权…",
        ["Edit.NoHelper"] = "未找到 DesktopIniHelper.exe，无法对受保护目录提权写入。",
        ["Edit.Failed.AccessDenied"] = "拒绝访问（可能需管理员权限）",
        ["Edit.Failed.Generic"] = "写入 desktop.ini 失败",
        ["Edit.Failed.NoElevationResult"] = "提权进程未返回结果",
        ["Edit.Restoring"] = "正在恢复默认…",
        ["Edit.RestoreDefault"] = "恢复默认",

        // 历史记录页
        ["History.Title"] = "历史记录",
        ["History.Heading"] = "修改与恢复记录",
        ["History.Description"] = "近期对文件夹别名/图标的改动会被记录在此，可还原任意一项。",
        ["History.Empty"] = "还没有任何记录",
        ["History.Before"] = "以前：",
        ["History.After"] = "之后：",
        ["History.Restore"] = "还原",
        ["History.Remove"] = "删除本条",
        ["History.Clear"] = "清空全部",
        ["History.Action.Modify"] = "修改",
        ["History.Action.Restore"] = "还原",
    };

    private static Dictionary<string, string> EnUs() => new()
    {
        ["App.Title"] = "WFAM · Windows Folder Alias Manager",

        // Admin warning
        ["Admin.Warning.Title"] = "Running as Administrator",
        ["Admin.Warning.Subtitle"] = "This application should not be launched with elevated privileges. Operations that need elevation invoke a separate DesktopIniHelper.exe via UAC. Running the main process as Administrator breaks drag-and-drop (Explorer cannot communicate with high-integrity processes) and adds unnecessary risk. Please close this window and restart as a standard user.",

        ["Nav.Folders"] = "Folders",
        ["Nav.Drives"] = "USB Drives",
        ["Nav.Settings"] = "Settings",
        ["Nav.About"] = "About",
        ["Nav.History"] = "History",

        ["Folders.Title"] = "Folders",
        ["Folders.Heading"] = "Folder Aliases & Icons",
        ["Folders.Description"] = "Drag folders into the drop zone, or click Import. Edit alias/icon, then save.",
        ["Folders.DropPrimary"] = "Drop folders here",
        ["Folders.DropSecondary"] = "Multi-select supported; existing aliases/icons are read automatically",
        ["Folders.Import"] = "Import folders",
        ["Folders.AutoScanIcons"] = "Auto-scan icons",
        ["Folders.Empty.Title"] = "No folders yet",
        ["Folders.Empty.Description"] = "Import or drop folders to start managing their aliases",
        ["Folders.AliasPlaceholder"] = "Enter folder alias…",
        ["Folders.Tip.PickIcon"] = "Pick a custom icon",
        ["Folders.Tip.OpenInExplorer"] = "Open in Explorer",
        ["Folders.Tip.Remove"] = "Remove",
        ["Folders.Tip.RestoreDefault"] = "Restore default alias and icon",
        ["Folders.Restoring"] = "Restoring…",
        ["Folders.Clear"] = "Clear",
        ["Folders.RefreshIcons"] = "Refresh icons",
        ["Folders.Save"] = "Save changes",

        // USB Drives page
        ["Drives.Title"] = "USB Drives",
        ["Drives.Heading"] = "Customize autorun.inf · Label & Icon",
        ["Drives.Description"] = "Write autorun.inf onto removable drives (USB sticks) to customize the name and icon shown in Explorer. The icon is copied to the drive root as a .ico so it follows the drive across machines.",
        ["Drives.Hint.Title"] = "Why isn't the drive scanned automatically?",
        ["Drives.Hint.Subtitle"] = "Scanning a removable medium would recursively read every .exe / .dll on it to extract icons. On large drives this generates significant I/O and may freeze the UI for a long time. Use the icon picker on the right to choose a .ico / .exe / .dll manually.",
        ["Drives.Refresh"] = "Rescan",
        ["Drives.IncludeFixed"] = "Show local drives",
        ["Drives.LabelPlaceholder"] = "Enter drive label…",
        ["Drives.Empty.Title"] = "No configurable drives found",
        ["Drives.Empty.Description"] = "Insert a USB drive or click \"Rescan\".",
        ["Drives.Tip.PickIcon"] = "Pick a custom icon (.ico/.exe/.dll)",
        ["Drives.Tip.RestoreDefault"] = "Restore default (delete autorun.inf)",
        ["Drives.Tip.OpenInExplorer"] = "Open in Explorer",
        ["Drives.Save"] = "Save to drives",
        ["Drives.Busy.Scanning"] = "Scanning drives…",
        ["Drives.Busy.ScanningIcons"] = "Scanning icons in {0}…",
        ["Drives.Busy.Saving"] = "Writing autorun.inf…",
        ["Drives.Busy.Restoring"] = "Restoring…",

        // Drive types
        ["DriveType.Removable"] = "Removable",
        ["DriveType.Fixed"] = "Local disk",
        ["DriveType.Network"] = "Network",
        ["DriveType.CDRom"] = "CD/DVD",
        ["DriveType.Ram"] = "RAM disk",
        ["DriveType.NoRootDirectory"] = "Unmounted",
        ["DriveType.Unknown"] = "Unknown",

        ["Settings.Title"] = "Settings",
        ["Settings.Group.Appearance"] = "Appearance",
        ["Settings.Theme"] = "Theme",
        ["Settings.Theme.Description"] = "Light / Dark / High Contrast, or follow the system.",
        ["Settings.Theme.System"] = "Use system setting",
        ["Settings.Theme.Light"] = "Light",
        ["Settings.Theme.Dark"] = "Dark",
        ["Settings.Theme.HighContrast"] = "High contrast",
        ["Settings.Group.Language"] = "Language",
        ["Settings.Language"] = "UI Language",
        ["Settings.Language.Description"] = "Switch the interface language (applied instantly).",
        ["Settings.Group.Integration"] = "System Integration",
        ["Settings.ContextMenu"] = "Folder context menu",
        ["Settings.ContextMenu.Description"] = "Add a \"Manage aliases with WFAM\" entry to the folder right-click menu.",
        ["Settings.ContextMenu.On"] = "Registered",
        ["Settings.ContextMenu.Off"] = "Not registered",
        ["Settings.ContextMenu.Register"] = "Register",
        ["Settings.ContextMenu.Unregister"] = "Unregister",
        ["Settings.ContextMenu.MenuLabel"] = "Manage aliases with WFAM",

        // Update
        ["Settings.Group.Update"] = "Updates",
        ["Settings.Update"] = "Check for updates",
        ["Settings.Update.Description"] = "Pull the latest release from GitHub.",
        ["Settings.Update.Auto"] = "Check for updates automatically on startup",
        ["Settings.Update.CurrentVersion"] = "Current version:",
        ["Update.CheckNow"] = "Check now",
        ["Update.Checking"] = "Checking for updates…",
        ["Update.UpToDate"] = "You're on the latest version.",
        ["Update.Available"] = "Update available",
        ["Update.Available.Format"] = "New version {0} is available. Click \"Open release page\" to download.",
        ["Update.OpenRelease"] = "Open release page",
        ["Update.UpdateNow"] = "Update now",
        ["Update.SkipThisVersion"] = "Skip this version",
        ["Update.Skipped"] = "This version will be skipped.",
        ["Update.CheckFailed"] = "Update check failed (check your network).",
        ["Update.OpenRepository"] = "Open project page",
        ["Update.Notify.NewVersion"] = "WFAM {0} is available — see Settings for details.",

        // Update dialog
        ["Update.Dialog.Title"] = "Update available: {0}",
        ["Update.Dialog.Subtitle"] = "WFAM {0} is available (current {1}). Choosing \"Update now\" will download and restart automatically.",
        ["Update.Dialog.ReleaseNotes"] = "Release notes:",
        ["Update.Dialog.NoNotes"] = "(This release ships without release notes.)",
        ["Update.Dialog.UpdateNow"] = "Update now",
        ["Update.Dialog.Later"] = "Remind me later",
        ["Update.Dialog.Skip"] = "Skip this version",
        ["Update.Dialog.Downloading.Title"] = "Downloading update",
        ["Update.Downloading"] = "Downloading… {0}%",
        ["Update.Failed"] = "Update failed",
        ["Update.NoAsset"] = "This release does not provide a downloadable .zip / .exe / .msi asset.",

        ["About.Title"] = "About",
        ["About.AppName"] = "WFAM · Folder Alias Manager",
        ["About.VersionFormat"] = "Version",
        ["About.Description"] =
            "A modern WPF UI for batch-configuring Windows folder localized names (LocalizedResourceName) " +
            "and custom icons (IconResource). Built on .NET 10, WPF-UI and MVVM.",
        ["About.Tech"] = "Tech stack",
        ["About.Tech.Body"] = ".NET 10 · WPF · WPF-UI · CommunityToolkit.Mvvm · Microsoft.Extensions.*",
        ["About.OpenWpfUi"] = "WPF-UI project page",

        ["Common.Yes"] = "Yes",
        ["Common.No"] = "No",
        ["Common.Cancel"] = "Cancel",
        ["Common.Success"] = "Success",
        ["Common.Failed"] = "Failed",
        ["Notify.ContextMenu.Registered"] = "Added to Explorer's right-click menu.",
        ["Notify.ContextMenu.Unregistered"] = "Removed from the right-click menu.",
        ["Notify.ContextMenu.Failed"] = "Failed to update context menu: {0}",

        // Edit window
        ["Edit.Title"] = "Edit folder alias",
        ["Edit.Alias"] = "Alias",
        ["Edit.Icon"] = "Icon",
        ["Edit.Loading"] = "Loading…",
        ["Edit.Saving"] = "Saving…",
        ["Edit.WaitingUac"] = "Waiting for UAC consent…",
        ["Edit.NoHelper"] = "DesktopIniHelper.exe was not found; cannot elevate write to a protected folder.",
        ["Edit.Failed.AccessDenied"] = "Access denied (administrator rights may be required)",
        ["Edit.Failed.Generic"] = "Failed to write desktop.ini",
        ["Edit.Failed.NoElevationResult"] = "Elevated helper returned no result",
        ["Edit.Restoring"] = "Restoring…",
        ["Edit.RestoreDefault"] = "Restore default",

        // History page
        ["History.Title"] = "History",
        ["History.Heading"] = "Modifications & restores",
        ["History.Description"] = "Recent changes are recorded here. You can revert any entry to its previous state.",
        ["History.Empty"] = "No history yet",
        ["History.Before"] = "Before:",
        ["History.After"] = "After:",
        ["History.Restore"] = "Restore",
        ["History.Remove"] = "Remove this entry",
        ["History.Clear"] = "Clear all",
        ["History.Action.Modify"] = "Modify",
        ["History.Action.Restore"] = "Restore",
    };
}
