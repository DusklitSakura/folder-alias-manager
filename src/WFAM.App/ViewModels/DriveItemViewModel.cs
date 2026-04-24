using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WFAM.App.Models;
using WFAM.App.Services;
namespace WFAM.App.ViewModels;

/// <summary>
/// 单个驱动器条目（绑定到 USB 列表卡片）。
/// </summary>
public partial class DriveItemViewModel : ObservableObject
{
    [ObservableProperty] private string _root = string.Empty;            // "G:\"
    [ObservableProperty] private string _displayName = string.Empty;     // "G:\ (KINGSTON)"
    [ObservableProperty] private string _driveTypeText = string.Empty;
    [ObservableProperty] private string _capacityText = string.Empty;    // "12.4 GB free / 30.0 GB"
    [ObservableProperty] private string _systemLabel = string.Empty;     // 文件系统层 label

    [ObservableProperty] private string _label = string.Empty;           // 用户编辑的别名（写入 autorun.inf）
    [ObservableProperty] private IconEntry? _selectedIcon;
    [ObservableProperty] private AutorunInfInfo? _originalInf;

    public ObservableCollection<IconEntry> AvailableIcons { get; } = new();

    public static DriveItemViewModel Create(
        DriveSnapshot snapshot,
        AutorunInfInfo inf,
        IEnumerable<IconEntry> icons)
    {
        var name = string.IsNullOrEmpty(snapshot.VolumeLabel)
            ? snapshot.Root
            : $"{snapshot.Root} ({snapshot.VolumeLabel})";

        var loc = LocalizationService.Instance;
        string typeText = loc[$"DriveType.{snapshot.DriveType}"];

        string capacity = snapshot.IsReady
            ? $"{FormatBytes(snapshot.FreeSpace)} 可用 / {FormatBytes(snapshot.TotalSize)}"
            : "未就绪";

        var vm = new DriveItemViewModel
        {
            Root = snapshot.Root,
            DisplayName = name,
            DriveTypeText = typeText,
            CapacityText = capacity,
            SystemLabel = snapshot.VolumeLabel,
            Label = string.IsNullOrWhiteSpace(inf.Label)
                ? (string.IsNullOrEmpty(snapshot.VolumeLabel) ? string.Empty : snapshot.VolumeLabel)
                : inf.Label!,
            OriginalInf = inf,
        };
        foreach (var ic in icons) vm.AvailableIcons.Add(ic);

        // 优先选中已被 autorun.inf 引用的同源图标；否则回落到第一项（默认文件夹）
        IconEntry? match = null;
        if (!string.IsNullOrEmpty(inf.IconPath))
        {
            string refFull = System.IO.Path.IsPathRooted(inf.IconPath)
                ? inf.IconPath!
                : System.IO.Path.Combine(snapshot.Root, inf.IconPath!);
            match = vm.AvailableIcons.FirstOrDefault(e =>
                !e.IsDefault &&
                string.Equals(e.SourcePath, refFull, StringComparison.OrdinalIgnoreCase) &&
                e.Index == inf.IconIndex);
        }
        vm.SelectedIcon = match ?? vm.AvailableIcons.FirstOrDefault();
        return vm;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }
}
