using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WFAM.App.Helpers;
using WFAM.App.Models;
using WFAM.App.Services;

namespace WFAM.App.ViewModels;

/// <summary>
/// 文件夹页面的 ViewModel。负责导入、扫描图标、保存别名/图标。
/// </summary>
public partial class FoldersViewModel : ObservableObject
{
    private readonly IDesktopIniService _ini;
    private readonly IIconService _icons;
    private readonly IElevationService _elevation;
    private readonly IShellService _shell;
    private readonly INotificationService _notify;
    private readonly IFolderPickerService _picker;
    private readonly IHistoryService _history;
    private readonly ILocalizationService _loc;
    private readonly ISettingsService _settings;
    private readonly ILogger<FoldersViewModel> _logger;

    public FoldersViewModel(
        IDesktopIniService ini,
        IIconService icons,
        IElevationService elevation,
        IShellService shell,
        INotificationService notify,
        IFolderPickerService picker,
        IHistoryService history,
        ILocalizationService loc,
        ISettingsService settings,
        ILogger<FoldersViewModel> logger)
    {
        _ini = ini; _icons = icons; _elevation = elevation;
        _shell = shell; _notify = notify; _picker = picker;
        _history = history; _loc = loc; _settings = settings; _logger = logger;
    }

    public ObservableCollection<FolderItemViewModel> Folders { get; } = new();

    [ObservableProperty] private bool _autoScanIcons = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshIconsCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _busyMessage = string.Empty;

    [ObservableProperty] private FolderItemViewModel? _selectedFolder;

    public bool HasFolders => Folders.Count > 0;

    public FoldersViewModel() : this(
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!) { }

    // ----- 命令 -----

    [RelayCommand]
    private async Task PickAndAddAsync()
    {
        var paths = _picker.PickFolders();
        if (paths.Count == 0) return;
        await AddFoldersAsync(paths);
    }

    [RelayCommand(CanExecute = nameof(CanRunAction))]
    private void Clear()
    {
        Folders.Clear();
        OnPropertyChanged(nameof(HasFolders));
    }

    [RelayCommand(CanExecute = nameof(CanRunAction))]
    private async Task RefreshIconsAsync()
    {
        IsBusy = true;
        try
        {
            BusyMessage = "正在重新扫描图标…";
            foreach (var f in Folders.ToList())
            {
                var icons = await _icons.CollectIconsForFolderAsync(f.Path);
                var previous = f.SelectedIcon;
                f.AvailableIcons.Clear();
                foreach (var ic in icons) f.AvailableIcons.Add(ic);
                f.SelectedIcon = f.AvailableIcons.FirstOrDefault(
                                     i => i.SourcePath == previous?.SourcePath && i.Index == previous?.Index)
                                 ?? f.AvailableIcons.FirstOrDefault();
            }
            _notify.Success("完成", "图标已重新扫描。");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRunAction))]
    private async Task SaveAsync()
    {
        if (Folders.Count == 0) return;

        IsBusy = true;
        var success = 0;
        var failed = new List<string>();
        var denied = new List<ElevatedWriteRequest>();

        try
        {
            BusyMessage = "正在保存…";
            // 记录各项原始状态以便写入历史
            var snapshots = Folders.ToDictionary(f => f.Path, f =>
            {
                var ini = f.OriginalIni;
                return (ini?.Alias ?? string.Empty, ini?.IconPath ?? string.Empty, ini?.IconIndex ?? 0);
            }, StringComparer.OrdinalIgnoreCase);

            foreach (var f in Folders)
            {
                var rawIconPath = f.SelectedIcon is { IsDefault: false } e ? e.SourcePath : null;
                var rawIconIdx = f.SelectedIcon is { IsDefault: false } e2 ? e2.Index : 0;
                var (iconPath, iconIdx) = IconStaging.ResolveIconPath(f.Path, rawIconPath, rawIconIdx, _settings.Current.CopyIconToFolder);
                try
                {
                    var result = await _ini.WriteAsync(f.Path, f.Alias, iconPath, iconIdx, f.BackgroundImage);
                    switch (result.Outcome)
                    {
                        case WriteOutcome.Success:
                            success++;
                            AppendHistory(f, snapshots, iconPath, iconIdx);
                            break;
                        case WriteOutcome.AccessDenied:
                            denied.Add(new ElevatedWriteRequest(f.Path, f.Name, f.Alias, iconPath, iconIdx, f.BackgroundImage));
                            break;
                        default:
                            failed.Add(string.IsNullOrWhiteSpace(result.Message) ? f.Name : $"{f.Name} ({result.Message})");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "写入失败 {path}", f.Path);
                    failed.Add(f.Name);
                }
            }

            _shell.NotifyAssocChanged();

            if (denied.Count > 0)
            {
                if (!_elevation.IsHelperAvailable)
                {
                    _notify.Warning("需要管理员权限",
                        $"{denied.Count} 个文件夹需要提权，但未找到 DesktopIniHelper.exe。");
                }
                else
                {
                    BusyMessage = $"等待 UAC 授权（{denied.Count} 个文件夹）…";
                    var results = await _elevation.ElevatedBatchWriteAsync(denied);
                    foreach (var r in results)
                    {
                        if (r.Outcome == WriteOutcome.Success)
                        {
                            success++;
                            var f = Folders.FirstOrDefault(x => x.Path == r.FolderPath);
                            if (f is not null)
                            {
                                var rawIconPath = f.SelectedIcon is { IsDefault: false } e ? e.SourcePath : null;
                                var rawIconIdx = f.SelectedIcon is { IsDefault: false } e2 ? e2.Index : 0;
                                var (iconPath, iconIdx) = IconStaging.ResolveIconPath(f.Path, rawIconPath, rawIconIdx, _settings.Current.CopyIconToFolder);
                                AppendHistory(f, snapshots, iconPath, iconIdx);
                            }
                        }
                        else failed.Add(string.IsNullOrWhiteSpace(r.Message) ? r.Name : $"{r.Name} ({r.Message})");
                    }
                    _shell.NotifyAssocChanged();
                }
            }

            if (failed.Count == 0)
                _notify.Success("保存成功", $"已更新 {success} 个文件夹。");
            else
                _notify.Warning("部分失败",
                    $"成功 {success} 个，失败 {failed.Count} 个：{string.Join("、", failed.Take(8))}{(failed.Count > 8 ? "…" : "")}");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenInExplorer(FolderItemViewModel? item)
    {
        if (item is null || !Directory.Exists(item.Path)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{item.Path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "无法打开 Explorer");
        }
    }

    [RelayCommand]
    private void Remove(FolderItemViewModel? item)
    {
        if (item is null) return;
        Folders.Remove(item);
        OnPropertyChanged(nameof(HasFolders));
    }

    [RelayCommand]
    private void PickCustomIcon(FolderItemViewModel? item)
    {
        if (item is null) return;
        var file = _picker.PickIconFile();
        if (string.IsNullOrEmpty(file)) return;
        var entry = _icons.ExtractSingle(file, 0);
        if (entry is null)
        {
            _notify.Warning("无法读取图标", file);
            return;
        }
        item.AvailableIcons.Add(entry);
        item.SelectedIcon = entry;
    }

    [RelayCommand]
    private void PickBackground(FolderItemViewModel? item)
    {
        if (item is null) return;
        var file = _picker.PickImageFile();
        if (string.IsNullOrEmpty(file)) return;
        item.BackgroundImage = file;
    }

    [RelayCommand]
    private void ClearBackground(FolderItemViewModel? item)
    {
        if (item is null) return;
        item.BackgroundImage = null;
    }

    private bool CanRunAction() => !IsBusy && Folders.Count > 0;

    private void AppendHistory(
        FolderItemViewModel f,
        IReadOnlyDictionary<string, (string Alias, string IconPath, int IconIndex)> snapshots,
        string? newIconPath,
        int newIconIndex)
    {
        if (_history is null) return;
        snapshots.TryGetValue(f.Path, out var before);
        _history.Add(new HistoryEntry
        {
            Action = HistoryAction.Modify,
            FolderPath = f.Path,
            FolderName = f.Name,
            BeforeAlias = before.Alias,
            BeforeIconPath = before.IconPath,
            BeforeIconIndex = before.IconIndex,
            AfterAlias = f.Alias,
            AfterIconPath = newIconPath ?? string.Empty,
            AfterIconIndex = newIconIndex,
        });
        // 更新快照，下次保存 before 指向当前状态
        f.OriginalIni = new DesktopIniInfo(f.Alias, newIconPath, newIconIndex);
    }

    [RelayCommand]
    private async Task RestoreDefaultAsync(FolderItemViewModel? item)
    {
        if (item is null) return;
        IsBusy = true;
        try
        {
            BusyMessage = _loc?["Folders.Restoring"] ?? "正在恢复默认…";
            var before = item.OriginalIni;
            var result = await _ini.RestoreAsync(item.Path);
            var outcome = result.Outcome;
            string? failMessage = result.Message;
            if (outcome == WriteOutcome.AccessDenied && _elevation.IsHelperAvailable)
            {
                BusyMessage = _loc?["Edit.WaitingUac"] ?? "等待 UAC…";
                var r = await _elevation.ElevatedBatchWriteAsync(new[]
                {
                    new ElevatedWriteRequest(item.Path, item.Name, string.Empty, null, 0, Restore: true),
                });
                if (r.Count > 0) { outcome = r[0].Outcome; failMessage = r[0].Message; }
                else { outcome = WriteOutcome.Failed; failMessage = "Helper 未返回结果"; }
            }

            if (outcome != WriteOutcome.Success)
            {
                var detail = string.IsNullOrWhiteSpace(failMessage) ? item.Name : $"{item.Name}: {failMessage}";
                _notify.Warning(_loc?["Common.Failed"] ?? "失败", detail);
                return;
            }

            _shell.NotifyAssocChanged();
            _history?.Add(new HistoryEntry
            {
                Action = HistoryAction.Restore,
                FolderPath = item.Path,
                FolderName = item.Name,
                BeforeAlias = before?.Alias ?? string.Empty,
                BeforeIconPath = before?.IconPath ?? string.Empty,
                BeforeIconIndex = before?.IconIndex ?? 0,
                AfterAlias = string.Empty,
                AfterIconPath = string.Empty,
                AfterIconIndex = 0,
            });

            // 重读以同步 UI
            var fresh = await _ini.ReadAsync(item.Path);
            item.OriginalIni = fresh;
            item.Alias = string.IsNullOrWhiteSpace(fresh.Alias) ? item.Name : fresh.Alias!;
            item.SelectedIcon = item.AvailableIcons.FirstOrDefault();

            _notify.Success(_loc?["Common.Success"] ?? "成功", item.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复默认失败 {p}", item.Path);
            _notify.Warning(_loc?["Common.Failed"] ?? "失败", ex.Message);
        }
        finally { IsBusy = false; }
    }

    // ----- 公共：从拖放/选择添加 -----

    public async Task AddFoldersAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        IsBusy = true;
        try
        {
            BusyMessage = $"正在导入 {paths.Count} 个文件夹…";
            foreach (var p in paths)
            {
                if (!Directory.Exists(p)) continue;
                if (Folders.Any(f => string.Equals(f.Path, p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var ini = await _ini.ReadAsync(p);
                IEnumerable<IconEntry> icons;
                if (AutoScanIcons)
                    icons = await _icons.CollectIconsForFolderAsync(p);
                else
                    icons = new[] { _icons.GetDefaultFolderIcon() };

                var vm = FolderItemViewModel.Create(p, ini, icons);
                Folders.Add(vm);
            }
            OnPropertyChanged(nameof(HasFolders));
            SaveCommand.NotifyCanExecuteChanged();
            ClearCommand.NotifyCanExecuteChanged();
            RefreshIconsCommand.NotifyCanExecuteChanged();
        }
        finally { IsBusy = false; }
    }
}
