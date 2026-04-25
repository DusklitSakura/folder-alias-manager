namespace WFAM.App.Models;

/// <summary>
/// 解析自 <drive>\autorun.inf 的 [autorun] 段。
/// IconPath 保留原始字面量（可能是相对盘符根的相对路径，如 "autorun.ico"）。
/// <para>
/// <see cref="BackgroundImage"/> 实际来自盘符根的 desktop.ini
/// （[{BE098140-A513-11D0-A3A4-00C04FD706EC}].IconArea_Image），
/// 由 <see cref="WFAM.App.Services.IAutorunInfService"/> 在写入 autorun.inf 时一并维护。
/// </para>
/// </summary>
public sealed record AutorunInfInfo(
    string? Label,
    string? IconPath,
    int IconIndex,
    string? BackgroundImage = null);
