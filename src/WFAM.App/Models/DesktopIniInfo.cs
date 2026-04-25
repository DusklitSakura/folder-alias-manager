namespace WFAM.App.Models;

/// <summary>
/// desktop.ini 解析得到的元数据。
/// <para>
/// <see cref="BackgroundImage"/> 来自 [{BE098140-A513-11D0-A3A4-00C04FD706EC}].IconArea_Image，
/// 是经典 Windows XP/Vista 文件夹自定义背景的字段。
/// </para>
/// </summary>
public sealed record DesktopIniInfo(
    string? Alias,
    string? IconPath,
    int IconIndex,
    string? BackgroundImage = null);
