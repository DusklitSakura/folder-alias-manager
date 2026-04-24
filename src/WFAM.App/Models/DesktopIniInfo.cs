namespace WFAM.App.Models;

/// <summary>
/// desktop.ini 解析得到的元数据。
/// </summary>
public sealed record DesktopIniInfo(string? Alias, string? IconPath, int IconIndex);
