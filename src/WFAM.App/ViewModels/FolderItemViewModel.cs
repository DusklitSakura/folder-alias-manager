using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using WFAM.App.Models;

namespace WFAM.App.ViewModels;

/// <summary>
/// 单个文件夹条目（绑定到列表/卡片）。
/// </summary>
public partial class FolderItemViewModel : ObservableObject
{
    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private IconEntry? _selectedIcon;
    [ObservableProperty] private DesktopIniInfo? _originalIni;

    public ObservableCollection<IconEntry> AvailableIcons { get; } = new();

    public static FolderItemViewModel Create(string folderPath, DesktopIniInfo ini, IEnumerable<IconEntry> icons)
    {
        var name = System.IO.Path.GetFileName(
            folderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        var vm = new FolderItemViewModel
        {
            Path = folderPath,
            Name = name,
            Alias = string.IsNullOrWhiteSpace(ini.Alias) ? name : ini.Alias!,
            OriginalIni = ini,
        };
        foreach (var ic in icons) vm.AvailableIcons.Add(ic);

        // 选中已有图标
        IconEntry? match = null;
        if (!string.IsNullOrEmpty(ini.IconPath))
        {
            match = vm.AvailableIcons.FirstOrDefault(
                e => string.Equals(e.SourcePath, ini.IconPath, StringComparison.OrdinalIgnoreCase)
                     && e.Index == ini.IconIndex);
        }
        vm.SelectedIcon = match ?? vm.AvailableIcons.FirstOrDefault();
        return vm;
    }
}
