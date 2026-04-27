using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WFAM.App.Models;
using WFAM.App.Services;

namespace WFAM.App.ViewModels;

/// <summary>
/// 文件夹伪装页 ViewModel。把目标文件夹的 desktop.ini 写入 [.ShellClassInfo].CLSID
/// 让 Explorer 把它呈现为系统命名空间对象（回收站、控制面板…）。
/// </summary>
public partial class DisguiseViewModel : ObservableObject
{
    private readonly IFolderDisguiseService _disguise;
    private readonly IFolderPickerService _picker;
    private readonly INotificationService _notify;
    private readonly ILocalizationService _loc;
    private readonly IShellService _shell;
    private readonly IElevationService _elevation;
    private readonly IHistoryService _history;
    private readonly ILogger<DisguiseViewModel> _logger;

    public DisguiseViewModel(
        IFolderDisguiseService disguise,
        IFolderPickerService picker,
        INotificationService notify,
        ILocalizationService loc,
        IShellService shell,
        IElevationService elevation,
        IHistoryService history,
        ILogger<DisguiseViewModel> logger)
    {
        _disguise = disguise;
        _picker = picker;
        _notify = notify;
        _loc = loc;
        _shell = shell;
        _elevation = elevation;
        _history = history;
        _logger = logger;

        Presets = new ObservableCollection<DisguisePresetItem>(
            _disguise.Presets.Select(p => new DisguisePresetItem(p, loc)));
        SelectedPreset = Presets.FirstOrDefault();
    }

    // 设计期无参构造，避免 XAML 设计器报错
    public DisguiseViewModel() : this(null!, null!, null!, null!, null!, null!, null!, null!) { }

    public ObservableCollection<DisguiseItemViewModel> Folders { get; } = new();

    public ObservableCollection<DisguisePresetItem> Presets { get; } = new();

    [ObservableProperty] private DisguisePresetItem? _selectedPreset;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = string.Empty;

    public bool HasFolders => Folders.Count > 0;

    [RelayCommand]
    private async Task PickAndAddAsync()
    {
        var paths = _picker.PickFolders();
        if (paths.Count == 0) return;
        await AddFoldersAsync(paths);
    }

    public async Task AddFoldersAsync(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0) return;
        IsBusy = true;
        try
        {
            BusyMessage = _loc["Disguise.Busy.Detecting"];
            await Task.Run(() =>
            {
                foreach (var p in paths)
                {
                    if (!Directory.Exists(p)) continue;
                    if (Folders.Any(f => string.Equals(f.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
                    var state = _disguise.Detect(p);
                    var vm = new DisguiseItemViewModel(p, _loc, state);
                    System.Windows.Application.Current.Dispatcher.Invoke(() => Folders.Add(vm));
                }
            });
            OnPropertyChanged(nameof(HasFolders));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Clear()
    {
        Folders.Clear();
        OnPropertyChanged(nameof(HasFolders));
    }

    [RelayCommand]
    private void Remove(DisguiseItemViewModel? item)
    {
        if (item is null) return;
        Folders.Remove(item);
        OnPropertyChanged(nameof(HasFolders));
    }

    [RelayCommand]
    private async Task DisguiseAsync(DisguiseItemViewModel? item)
    {
        if (item is null) return;
        var preset = item.SelectedPreset ?? SelectedPreset;
        if (preset is null)
        {
            _notify.Warning(_loc["Common.Failed"], _loc["Disguise.NoPresetSelected"]);
            return;
        }
        await ApplyAsync(new[] { item }, preset);
    }

    [RelayCommand]
    private async Task DisguiseAllAsync()
    {
        if (Folders.Count == 0) return;
        if (SelectedPreset is null)
        {
            _notify.Warning(_loc["Common.Failed"], _loc["Disguise.NoPresetSelected"]);
            return;
        }
        await ApplyAsync(Folders.ToList(), SelectedPreset);
    }

    [RelayCommand]
    private async Task RestoreAsync(DisguiseItemViewModel? item)
    {
        if (item is null) return;
        IsBusy = true;
        try
        {
            BusyMessage = _loc["Disguise.Busy.Restoring"];
            var before = item.State;
            var r = await _disguise.RestoreAsync(item.Path);
            var outcome = r.Outcome;
            string? failMessage = r.Message;

            if (outcome == WriteOutcome.AccessDenied && _elevation.IsHelperAvailable)
            {
                BusyMessage = _loc["Edit.WaitingUac"];
                var er = await _elevation.ElevatedBatchDisguiseAsync(new[]
                {
                    new ElevatedDisguiseRequest(item.Path, item.Name, string.Empty, Restore: true),
                });
                if (er.Count > 0) { outcome = er[0].Outcome; failMessage = er[0].Message; }
            }

            if (outcome == WriteOutcome.Success)
            {
                _history.Add(new HistoryEntry
                {
                    Action = HistoryAction.Restore,
                    FolderPath = item.Path,
                    FolderName = item.Name,
                    BeforeClsid = before.Clsid ?? string.Empty,
                    AfterClsid = string.Empty,
                });
                _notify.Success(_loc["Common.Success"], _loc["Disguise.Notify.Restored"]);
            }
            else
            {
                _notify.Warning(_loc["Common.Failed"],
                    string.IsNullOrWhiteSpace(failMessage) ? outcome.ToString() : failMessage);
            }

            item.State = _disguise.Detect(item.Path);
            _shell.NotifyAssocChanged();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenInExplorer(DisguiseItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            // 已伪装的目录在 Explorer 里点开会跳转到 shell 命名空间对象，
            // 这里以参数形式定位到父目录并选中它，方便用户取消伪装。
            var parent = Path.GetDirectoryName(item.Path);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.Path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "open explorer failed");
        }
    }

    private async Task ApplyAsync(IReadOnlyList<DisguiseItemViewModel> items, DisguisePresetItem preset)
    {
        IsBusy = true;
        var success = 0;
        var failed = new List<string>();
        // 记录每项原始状态供写入 history 使用
        var beforeMap = items.ToDictionary(
            i => i.Path,
            i => i.State.Clsid ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
        var elevated = new List<DisguiseItemViewModel>();
        try
        {
            BusyMessage = _loc["Disguise.Busy.Applying"];
            foreach (var i in items)
            {
                var r = await _disguise.DisguiseAsync(i.Path, preset.Clsid);
                switch (r.Outcome)
                {
                    case WriteOutcome.Success:
                        success++;
                        i.State = _disguise.Detect(i.Path);
                        AppendHistory(i, beforeMap, preset.Clsid);
                        break;
                    case WriteOutcome.AccessDenied:
                        elevated.Add(i);
                        break;
                    default:
                        failed.Add(string.IsNullOrWhiteSpace(r.Message) ? i.Name : $"{i.Name} ({r.Message})");
                        break;
                }
            }

            if (elevated.Count > 0)
            {
                if (!_elevation.IsHelperAvailable)
                {
                    foreach (var i in elevated)
                        failed.Add($"{i.Name} ({_loc["Edit.NoHelper"]})");
                }
                else
                {
                    BusyMessage = _loc["Edit.WaitingUac"];
                    var requests = elevated
                        .Select(i => new ElevatedDisguiseRequest(i.Path, i.Name, preset.Clsid))
                        .ToList();
                    var results = await _elevation.ElevatedBatchDisguiseAsync(requests);
                    foreach (var er in results)
                    {
                        var match = elevated.FirstOrDefault(x =>
                            string.Equals(x.Path, er.FolderPath, StringComparison.OrdinalIgnoreCase));
                        if (er.Outcome == WriteOutcome.Success && match is not null)
                        {
                            success++;
                            match.State = _disguise.Detect(match.Path);
                            AppendHistory(match, beforeMap, preset.Clsid);
                        }
                        else
                        {
                            failed.Add(string.IsNullOrWhiteSpace(er.Message) ? er.Name : $"{er.Name} ({er.Message})");
                        }
                    }
                }
            }

            _shell.NotifyAssocChanged();

            if (failed.Count == 0)
                _notify.Success(_loc["Common.Success"], string.Format(_loc["Disguise.Notify.Applied"], success, preset.DisplayName));
            else
                _notify.Warning(_loc["Common.Failed"],
                    string.Format(_loc["Disguise.Notify.Partial"], success, failed.Count,
                        string.Join("、", failed.Take(6)) + (failed.Count > 6 ? "…" : string.Empty)));
        }
        finally { IsBusy = false; }
    }

    private void AppendHistory(DisguiseItemViewModel item,
                               IReadOnlyDictionary<string, string> beforeMap,
                               string afterClsid)
    {
        beforeMap.TryGetValue(item.Path, out var beforeClsid);
        _history.Add(new HistoryEntry
        {
            Action = HistoryAction.Modify,
            FolderPath = item.Path,
            FolderName = item.Name,
            BeforeClsid = beforeClsid ?? string.Empty,
            AfterClsid = afterClsid,
        });
    }

    private void HandleResult(WriteResult r, bool restore)
    {
        // (kept for reference; Restore/Apply handle their own messaging now)
        if (r.Outcome == WriteOutcome.Success)
            _notify.Success(_loc["Common.Success"], restore ? _loc["Disguise.Notify.Restored"] : _loc["Disguise.Notify.AppliedSingle"]);
        else
            _notify.Warning(_loc["Common.Failed"], string.IsNullOrWhiteSpace(r.Message) ? r.Outcome.ToString() : r.Message);
    }
}
