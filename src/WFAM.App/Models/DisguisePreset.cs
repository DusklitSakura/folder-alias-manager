namespace WFAM.App.Models;

/// <summary>
/// 文件夹伪装预设：把目标文件夹的 desktop.ini 里 [.ShellClassInfo].CLSID
/// 设置为某个 Windows 命名空间对象的 CLSID，让 Explorer 把它当作系统对象呈现。
/// </summary>
/// <param name="NameKey">本地化键，用于在 UI 中显示伪装目标名称。</param>
/// <param name="Clsid">目标 CLSID，包含大括号，例如 "{645FF040-5081-101B-9F08-00AA002F954E}"。</param>
/// <param name="Symbol">列表里展示的 WPF-UI SymbolIcon 名称（仅用于 UI 提示，与目标外观无关）。</param>
public sealed record DisguisePreset(string NameKey, string Clsid, string Symbol);

/// <summary>
/// 一个文件夹的当前伪装状态。
/// </summary>
/// <param name="IsDisguised">desktop.ini 中是否检测到 [.ShellClassInfo].CLSID。</param>
/// <param name="Clsid">检测到的 CLSID（原始字符串），未伪装时为 null。</param>
/// <param name="MatchedPresetNameKey">命中已知预设时返回其本地化键，否则为 null（未知 CLSID）。</param>
public sealed record DisguiseState(bool IsDisguised, string? Clsid, string? MatchedPresetNameKey);
