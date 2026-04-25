using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WFAM.App.Models;
using WFAM.App.Services;

namespace WFAM.App.ViewModels;

/// <summary>
/// "U盘" 页面 ViewModel。负责枚举可移动盘 / 本地盘根目录、自定义 autorun.inf 的 label/icon。
/// </summary>
public partial class UsbDrivesViewModel : ObservableObject
{
    private readonly IAutorunInfService _autorun;
    private readonly IDriveService _drives;
    private readonly IIconService _icons;
    private readonly IElevationService _elevation;
    private readonly IShellService _shell;
    private readonly INotificationService _notify;
    private readonly IFolderPickerService _picker;
    private readonly ILocalizationService _loc;
    private readonly ILogger<UsbDrivesViewModel> _logger;

    public UsbDrivesViewModel(
        IAutorunInfService autorun,
        IDriveService drives,
        IIconService icons,
        IElevationService elevation,
        IShellService shell,
        INotificationService notify,
        IFolderPickerService picker,
        ILocalizationService loc,
        ILogger<UsbDrivesViewModel> logger)
    {
        _autorun = autorun; _drives = drives; _icons = icons;
        _elevation = elevation; _shell = shell; _notify = notify;
        _picker = picker; _loc = loc; _logger = logger;
    }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = new();

    [ObservableProperty] private bool _includeFixedDrives;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = string.Empty;
    [ObservableProperty] private bool _hasLoaded;

    public bool HasDrives => Drives.Count > 0;

    public UsbDrivesViewModel() : this(
        null!, null!, null!, null!, null!, null!, null!, null!, null!) { }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            BusyMessage = _loc?["Drives.Busy.Scanning"] ?? "正在扫描驱动器…";
            Drives.Clear();

            var snaps = _drives.Enumerate(IncludeFixedDrives);
            foreach (var s in snaps)
            {
                var inf = s.IsReady
                    ? await _autorun.ReadAsync(s.Root)
                    : new AutorunInfInfo(null, null, 0);

                var icons = new List<IconEntry> { _icons.GetDefaultFolderIcon() };

                // 主动扫描会递归读取整个可移动介质，易导致 UI 无响应。
                // 这里只读取 autorun.inf 中已经在用的图标（如果存在），
                // 其他图标请用户通过「选择自定义图标」手动拾取。
                if (s.IsReady && !string.IsNullOrEmpty(inf.IconPath))
                {
                    var existing = ResolveDriveIconCandidate(s.Root, inf.IconPath, inf.IconIndex);
                    if (existing is not null) icons.Add(existing);
                }

                Drives.Add(DriveItemViewModel.Create(s, inf, icons));
            }
            HasLoaded = true;
            OnPropertyChanged(nameof(HasDrives));
        }
        finally { IsBusy = false; }
    }

    partial void OnIncludeFixedDrivesChanged(bool value) => _ = RefreshAsync();

    /// <summary>
    /// 解析 autorun.inf 中的 icon=... 引用：
    /// - 只是文件名 (如 autorun.ico) → 拼接盘符根
    /// - 相对路径 → 拼接盘符根
    /// - 绝对路径 → 使用原值
    /// 返回一个对应的 IconEntry（提取失败返 null）。
    /// </summary>
    private IconEntry? ResolveDriveIconCandidate(string driveRoot, string iconRef, int index)
    {
        try
        {
            string full = System.IO.Path.IsPathRooted(iconRef)
                ? iconRef
                : System.IO.Path.Combine(driveRoot, iconRef);
            if (!System.IO.File.Exists(full)) return null;
            return _icons.ExtractSingle(full, index);
        }
        catch { return null; }
    }

    [RelayCommand]
    private void PickCustomIcon(DriveItemViewModel? item)
    {
        if (item is null) return;
        var file = _picker.PickIconFile();
        if (string.IsNullOrEmpty(file)) return;
        var entry = _icons.ExtractSingle(file, 0);
        if (entry is null)
        {
            _notify.Warning(_loc?["Common.Failed"] ?? "失败", file);
            return;
        }
        item.AvailableIcons.Add(entry);
        item.SelectedIcon = entry;
    }

    [RelayCommand]
    private void PickBackground(DriveItemViewModel? item)
    {
        if (item is null) return;
        var file = _picker.PickImageFile();
        if (string.IsNullOrEmpty(file)) return;
        item.BackgroundImage = file;
    }

    [RelayCommand]
    private void ClearBackground(DriveItemViewModel? item)
    {
        if (item is null) return;
        item.BackgroundImage = null;
    }

    [RelayCommand]
    private void OpenInExplorer(DriveItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{item.Root}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { _logger.LogWarning(ex, "无法打开 Explorer"); }
    }

    [RelayCommand]
    private async Task RestoreAsync(DriveItemViewModel? item)
    {
        if (item is null) return;
        IsBusy = true;
        try
        {
            BusyMessage = _loc?["Drives.Busy.Restoring"] ?? "正在恢复默认…";
            var result = await _autorun.RestoreAsync(item.Root);
            string? message = result.Message;
            var outcome = result.Outcome;

            if (outcome == WriteOutcome.AccessDenied && _elevation.IsHelperAvailable)
            {
                BusyMessage = _loc?["Edit.WaitingUac"] ?? "等待 UAC…";
                var r = await _elevation.ElevatedBatchAutorunAsync(new[]
                {
                    new ElevatedAutorunRequest(
                        DrivePath: item.Root,
                        Name: item.DisplayName,
                        Label: string.Empty,
                        StagedIconPath: null,
                        IconTargetName: _autorun.DriveIconFileName,
                        BackgroundImage: null,
                        Restore: true),
                });
                if (r.Count > 0) { outcome = r[0].Outcome; message = r[0].Message; }
                else { outcome = WriteOutcome.Failed; message = "Helper 未返回结果"; }
            }

            if (outcome != WriteOutcome.Success)
            {
                _notify.Warning(_loc?["Common.Failed"] ?? "失败",
                    string.IsNullOrWhiteSpace(message) ? item.DisplayName : $"{item.DisplayName}: {message}");
                return;
            }

            _shell.NotifyAssocChanged();
            // 同步 UI
            item.OriginalInf = await _autorun.ReadAsync(item.Root);
            item.Label = item.SystemLabel;
            item.SelectedIcon = item.AvailableIcons.FirstOrDefault();
            _notify.Success(_loc?["Common.Success"] ?? "成功", item.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复 autorun.inf 失败 {p}", item.Root);
            _notify.Warning(_loc?["Common.Failed"] ?? "失败", ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Drives.Count == 0) return;
        IsBusy = true;
        var success = 0;
        var failed = new List<string>();
        var deniedItems = new List<(DriveItemViewModel item, string? staged)>();

        try
        {
            BusyMessage = _loc?["Drives.Busy.Saving"] ?? "正在保存…";
            foreach (var d in Drives)
            {
                string? staged = null;
                try
                {
                    if (d.SelectedIcon is { IsDefault: false } e)
                    {
                        staged = await _autorun.StageIconAsync(e.SourcePath, e.Index);
                    }

                    var result = await _autorun.WriteAsync(d.Root, d.Label ?? string.Empty, staged, d.BackgroundImage);
                    switch (result.Outcome)
                    {
                        case WriteOutcome.Success:
                            success++;
                            // 同步快照
                            d.OriginalInf = new AutorunInfInfo(
                                d.Label,
                                staged is null ? null : _autorun.DriveIconFileName,
                                0);
                            // 写入成功后清理 staged
                            TryDelete(staged);
                            break;
                        case WriteOutcome.AccessDenied:
                            deniedItems.Add((d, staged));
                            break;
                        default:
                            failed.Add(string.IsNullOrWhiteSpace(result.Message) ? d.DisplayName : $"{d.DisplayName} ({result.Message})");
                            TryDelete(staged);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "写入 autorun.inf 失败 {p}", d.Root);
                    failed.Add(d.DisplayName);
                    TryDelete(staged);
                }
            }

            _shell.NotifyAssocChanged();

            if (deniedItems.Count > 0)
            {
                if (!_elevation.IsHelperAvailable)
                {
                    _notify.Warning(_loc?["Edit.NoHelper"] ?? "需要管理员权限",
                        $"{deniedItems.Count} 个驱动器需要提权，但未找到 Helper。");
                    foreach (var (_, staged) in deniedItems) TryDelete(staged);
                }
                else
                {
                    BusyMessage = $"等待 UAC 授权（{deniedItems.Count} 个）…";
                    var requests = deniedItems.Select(t => new ElevatedAutorunRequest(
                        DrivePath: t.item.Root,
                        Name: t.item.DisplayName,
                        Label: t.item.Label ?? string.Empty,
                        StagedIconPath: t.staged,
                        IconTargetName: _autorun.DriveIconFileName,
                        BackgroundImage: t.item.BackgroundImage,
                        Restore: false)).ToList();

                    var results = await _elevation.ElevatedBatchAutorunAsync(requests);
                    foreach (var r in results)
                    {
                        var pair = deniedItems.FirstOrDefault(x =>
                            string.Equals(x.item.Root, r.FolderPath, StringComparison.OrdinalIgnoreCase));
                        if (r.Outcome == WriteOutcome.Success)
                        {
                            success++;
                            if (pair.item is not null)
                            {
                                pair.item.OriginalInf = new AutorunInfInfo(
                                    pair.item.Label,
                                    pair.staged is null ? null : _autorun.DriveIconFileName,
                                    0);
                            }
                        }
                        else
                        {
                            failed.Add(string.IsNullOrWhiteSpace(r.Message) ? r.Name : $"{r.Name} ({r.Message})");
                        }
                    }
                    _shell.NotifyAssocChanged();
                }
            }

            if (failed.Count == 0)
                _notify.Success(_loc?["Common.Success"] ?? "成功",
                    $"已更新 {success} 个驱动器。");
            else
                _notify.Warning(_loc?["Common.Failed"] ?? "部分失败",
                    $"成功 {success} 个，失败 {failed.Count} 个：{string.Join("、", failed.Take(8))}{(failed.Count > 8 ? "…" : "")}");
        }
        finally { IsBusy = false; }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { /* ignore */ }
    }
}
