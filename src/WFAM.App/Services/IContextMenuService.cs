namespace WFAM.App.Services;

/// <summary>
/// Windows Explorer 文件夹右键菜单注册。
/// 写入 HKCU\Software\Classes\Directory\shell\WFAM —— 仅当前用户，无需管理员。
/// </summary>
public interface IContextMenuService
{
    /// <summary>当前是否已注册到右键菜单。</summary>
    bool IsRegistered { get; }

    /// <summary>注册右键菜单（覆盖式）。<paramref name="menuLabel"/> 为菜单显示文本。</summary>
    void Register(string menuLabel);

    /// <summary>移除右键菜单注册。</summary>
    void Unregister();
}
