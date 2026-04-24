using System.IO;

namespace WFAM.App.Services;

/// <summary>表示一块可配置的驱动器（移动盘 / 本地盘）。</summary>
public sealed record DriveSnapshot(
    string Root,           // 例如 "G:\"
    string VolumeLabel,    // 文件系统层 label（可能为空）
    DriveType DriveType,   // 原始枚举（UI 层做本地化）
    long TotalSize,
    long FreeSpace,
    bool IsReady);

public interface IDriveService
{
    /// <summary>枚举可用于自定义 autorun.inf 的驱动器（默认仅移动盘）。</summary>
    IReadOnlyList<DriveSnapshot> Enumerate(bool includeFixed = false);
}

public sealed class DriveService : IDriveService
{
    public IReadOnlyList<DriveSnapshot> Enumerate(bool includeFixed = false)
    {
        var sysRoot = Path.GetPathRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? string.Empty;
        var list = new List<DriveSnapshot>();

        DriveInfo[] all;
        try { all = DriveInfo.GetDrives(); }
        catch { return list; }

        foreach (var d in all)
        {
            // 跳过系统盘
            if (string.Equals(d.Name, sysRoot, StringComparison.OrdinalIgnoreCase)) continue;

            if (d.DriveType != DriveType.Removable
                && !(includeFixed && d.DriveType == DriveType.Fixed))
                continue;

            string label = string.Empty;
            long total = 0, free = 0;
            bool ready = false;
            try
            {
                ready = d.IsReady;
                if (ready)
                {
                    label = d.VolumeLabel ?? string.Empty;
                    total = d.TotalSize;
                    free = d.AvailableFreeSpace;
                }
            }
            catch { /* 忽略：未就绪驱动器 */ }

            list.Add(new DriveSnapshot(
                Root: d.Name,
                VolumeLabel: label,
                DriveType: d.DriveType,
                TotalSize: total,
                FreeSpace: free,
                IsReady: ready));
        }
        return list;
    }
}
