using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WFAM.App.Models;
using WFAM.App.Services;

namespace WFAM.App.ViewModels;

/// <summary>
/// 单个文件夹的快速编辑窗口的 ViewModel。
/// 由右键菜单调用 (--edit "&lt;path&gt;") 时使用。
/// </summary>
public partial class EditFolderViewModel : ObservableObject
{
    private readonly IDesktopIniService _ini;
    private readonly IIconService _icons;
    private readonly IElevationService _elevation;
    private readonly IShellService _shell;
    private readonly INotificationService _notify;
    private readonly IFolderPickerService _picker;
    private readonly IHistoryService _history;
    private readonly ILocalizationService _loc;
    private readonly ILogger<EditFolderViewModel> _logger;

    public EditFolderViewModel(
        IDesktopIniService ini,
        IIconService icons,
        IElevationService elevation,
        IShellService shell,
        INotificationService notify,
        IFolderPickerService picker,
        IHistoryService history,
        ILocalizationService loc,
        ILogger<EditFolderViewModel> logger)
    {
        _ini = ini; _icons = icons; _elevation = elevation; _shell = shell;
        _notify = notify; _picker = picker; _history = history; _loc = loc; _logger = logger;
    }

    [ObservableProperty] private FolderItemViewModel? _item;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = string.Empty;

    public bool HasItem => Item is not null;

    public async Task LoadAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            _notify.Warning(_loc["Common.Failed"], folderPath);
            return;
        }

        IsBusy = true;
        BusyMessage = _loc["Edit.Loading"];
        try
        {
            var info = await _ini.ReadAsync(folderPath);
            var icons = await _icons.CollectIconsForFolderAsync(folderPath);
            Item = FolderItemViewModel.Create(folderPath, info, icons);
            OnPropertyChanged(nameof(HasItem));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void PickCustomIcon()
    {
        if (Item is null) return;
        var f = _picker.PickIconFile();
        if (string.IsNullOrEmpty(f)) return;
        var entry = _icons.ExtractSingle(f, 0);
        if (entry is null)
        {
            _notify.Warning(_loc["Common.Failed"], f);
            return;
        }
        Item.AvailableIcons.Add(entry);
        Item.SelectedIcon = entry;
    }

    [RelayCommand]
    private async Task RestoreDefaultAsync()
    {
        if (Item is null) return;
        IsBusy = true;
        try
        {
            BusyMessage = _loc["Edit.Restoring"];
            var before = Item.OriginalIni;
            var result = await _ini.RestoreAsync(Item.Path);
            string? failMessage = result.Outcome == WriteOutcome.Success ? null : result.Message;
            if (result.Outcome == WriteOutcome.AccessDenied && _elevation.IsHelperAvailable)
            {
                BusyMessage = _loc["Edit.WaitingUac"];
                var r = await _elevation.ElevatedBatchWriteAsync(new[]
                {
                    new ElevatedWriteRequest(Item.Path, Item.Name, string.Empty, null, 0, Restore: true),
                });
                if (r.Count > 0) { result = r[0]; failMessage = result.Message; }
                else { result = result with { Outcome = WriteOutcome.Failed }; failMessage = _loc["Edit.Failed.NoElevationResult"]; }
            }
            if (result.Outcome != WriteOutcome.Success)
            {
                _notify.Warning(_loc["Common.Failed"], BuildFailMessage(Item.Name, result.Outcome, failMessage));
                return;
            }
            _shell.NotifyAssocChanged();
            _history.Add(new HistoryEntry
            {
                Action = HistoryAction.Restore,
                FolderPath = Item.Path,
                FolderName = Item.Name,
                BeforeAlias = before?.Alias ?? string.Empty,
                BeforeIconPath = before?.IconPath ?? string.Empty,
                BeforeIconIndex = before?.IconIndex ?? 0,
                AfterAlias = string.Empty,
                AfterIconPath = string.Empty,
                AfterIconIndex = 0,
            });
            var fresh = await _ini.ReadAsync(Item.Path);
            Item.OriginalIni = fresh;
            Item.Alias = string.IsNullOrWhiteSpace(fresh.Alias) ? Item.Name : fresh.Alias!;
            Item.SelectedIcon = Item.AvailableIcons.FirstOrDefault();
            _notify.Success(_loc["Common.Success"], Item.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复默认失败 {p}", Item.Path);
            _notify.Warning(_loc["Common.Failed"], $"{Item.Name}: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    /// <summary>保存。返回 true 表示成功（含提权后成功）。</summary>
    public async Task<bool> SaveAsync()
    {
        if (Item is null) return false;
        IsBusy = true;
        try
        {
            BusyMessage = _loc["Edit.Saving"];
            var iconPath = Item.SelectedIcon is { IsDefault: false } e ? e.SourcePath : null;
            var iconIdx = Item.SelectedIcon is { IsDefault: false } e2 ? e2.Index : 0;

            var result = await _ini.WriteAsync(Item.Path, Item.Alias, iconPath, iconIdx);
            string? failMessage = result.Outcome == WriteOutcome.Success ? null : result.Message;

            if (result.Outcome == WriteOutcome.AccessDenied)
            {
                if (!_elevation.IsHelperAvailable)
                {
                    _notify.Warning(_loc["Common.Failed"], $"{Item.Name}: {_loc["Edit.NoHelper"]}");
                    return false;
                }
                BusyMessage = _loc["Edit.WaitingUac"];
                var results = await _elevation.ElevatedBatchWriteAsync(new[]
                {
                    new ElevatedWriteRequest(Item.Path, Item.Name, Item.Alias, iconPath, iconIdx),
                });
                if (results.Count == 0)
                {
                    _notify.Warning(_loc["Common.Failed"], $"{Item.Name}: {_loc["Edit.Failed.NoElevationResult"]}");
                    return false;
                }
                result = results[0];
                failMessage = result.Message;
                if (result.Outcome != WriteOutcome.Success)
                {
                    _notify.Warning(_loc["Common.Failed"], BuildFailMessage(Item.Name, result.Outcome, failMessage));
                    return false;
                }
            }
            else if (result.Outcome != WriteOutcome.Success)
            {
                _notify.Warning(_loc["Common.Failed"], BuildFailMessage(Item.Name, result.Outcome, failMessage));
                return false;
            }

            _shell.NotifyAssocChanged();
            // 记录历史
            var before = Item.OriginalIni;
            _history.Add(new HistoryEntry
            {
                Action = HistoryAction.Modify,
                FolderPath = Item.Path,
                FolderName = Item.Name,
                BeforeAlias = before?.Alias ?? string.Empty,
                BeforeIconPath = before?.IconPath ?? string.Empty,
                BeforeIconIndex = before?.IconIndex ?? 0,
                AfterAlias = Item.Alias,
                AfterIconPath = iconPath ?? string.Empty,
                AfterIconIndex = iconIdx,
            });
            _notify.Success(_loc["Common.Success"], Item.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存失败 {path}", Item.Path);
            _notify.Warning(_loc["Common.Failed"], $"{Item.Name}: {ex.Message}");
            return false;
        }
        finally { IsBusy = false; }
    }

    /// <summary>拼接“名称: 具体原因”作为提示条详情。</summary>
    private string BuildFailMessage(string name, WriteOutcome outcome, string? detail)
    {
        var reason = !string.IsNullOrWhiteSpace(detail)
            ? detail!
            : outcome switch
            {
                WriteOutcome.AccessDenied => _loc["Edit.Failed.AccessDenied"],
                _ => _loc["Edit.Failed.Generic"],
            };
        return $"{name}: {reason}";
    }
}
