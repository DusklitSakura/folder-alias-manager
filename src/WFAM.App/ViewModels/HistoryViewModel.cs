using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WFAM.App.Models;
using WFAM.App.Services;

namespace WFAM.App.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService _history;
    private readonly IDesktopIniService _ini;
    private readonly IElevationService _elevation;
    private readonly IShellService _shell;
    private readonly INotificationService _notify;
    private readonly ILocalizationService _loc;
    private readonly ILogger<HistoryViewModel> _logger;

    public ObservableCollection<HistoryItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isBusy;

    public bool HasItems => Items.Count > 0;

    public HistoryViewModel(
        IHistoryService history,
        IDesktopIniService ini,
        IElevationService elevation,
        IShellService shell,
        INotificationService notify,
        ILocalizationService loc,
        ILogger<HistoryViewModel> logger)
    {
        _history = history; _ini = ini; _elevation = elevation;
        _shell = shell; _notify = notify; _loc = loc; _logger = logger;
        _history.Changed += (_, _) => Reload();
        Reload();
    }

    public HistoryViewModel() : this(null!, null!, null!, null!, null!, null!, null!) { }

    private void Reload()
    {
        Items.Clear();
        foreach (var e in _history.Entries) Items.Add(new HistoryItemViewModel(e, _loc));
        OnPropertyChanged(nameof(HasItems));
    }

    [RelayCommand]
    private void Remove(HistoryItemViewModel? item)
    {
        if (item is null) return;
        _history.Remove(item.Entry.Id);
    }

    [RelayCommand]
    private void Clear() => _history.Clear();

    [RelayCommand]
    private async Task RestoreAsync(HistoryItemViewModel? item)
    {
        if (item is null) return;
        var entry = item.Entry;
        if (!Directory.Exists(entry.FolderPath))
        {
            _notify.Warning(_loc["Common.Failed"], entry.FolderPath);
            return;
        }

        IsBusy = true;
        try
        {
            // 当前实际状态 → 写入 history 的“before”
            var current = await _ini.ReadAsync(entry.FolderPath);

            var hasBefore = !string.IsNullOrEmpty(entry.BeforeAlias) || !string.IsNullOrEmpty(entry.BeforeIconPath);

            WriteOutcome outcome;
            string? failMessage = null;
            if (!hasBefore)
            {
                var rs = await _ini.RestoreAsync(entry.FolderPath);
                outcome = rs.Outcome; failMessage = rs.Message;
                if (outcome == WriteOutcome.AccessDenied && _elevation.IsHelperAvailable)
                {
                    var r = await _elevation.ElevatedBatchWriteAsync(new[]
                    {
                        new ElevatedWriteRequest(entry.FolderPath, entry.FolderName, string.Empty, null, 0, Restore: true),
                    });
                    if (r.Count > 0) { outcome = r[0].Outcome; failMessage = r[0].Message; }
                    else { outcome = WriteOutcome.Failed; failMessage = "Helper 未返回结果"; }
                }
            }
            else
            {
                var iconPath = string.IsNullOrEmpty(entry.BeforeIconPath) ? null : entry.BeforeIconPath;
                var rs = await _ini.WriteAsync(entry.FolderPath, entry.BeforeAlias, iconPath, entry.BeforeIconIndex);
                outcome = rs.Outcome; failMessage = rs.Message;
                if (outcome == WriteOutcome.AccessDenied && _elevation.IsHelperAvailable)
                {
                    var r = await _elevation.ElevatedBatchWriteAsync(new[]
                    {
                        new ElevatedWriteRequest(entry.FolderPath, entry.FolderName, entry.BeforeAlias, iconPath, entry.BeforeIconIndex),
                    });
                    if (r.Count > 0) { outcome = r[0].Outcome; failMessage = r[0].Message; }
                    else { outcome = WriteOutcome.Failed; failMessage = "Helper 未返回结果"; }
                }
            }

            if (outcome != WriteOutcome.Success)
            {
                var detail = string.IsNullOrWhiteSpace(failMessage) ? entry.FolderName : $"{entry.FolderName}: {failMessage}";
                _notify.Warning(_loc["Common.Failed"], detail);
                return;
            }

            _shell.NotifyAssocChanged();

            _history.Add(new HistoryEntry
            {
                Action = HistoryAction.Restore,
                FolderPath = entry.FolderPath,
                FolderName = entry.FolderName,
                BeforeAlias = current.Alias ?? string.Empty,
                BeforeIconPath = current.IconPath ?? string.Empty,
                BeforeIconIndex = current.IconIndex,
                AfterAlias = entry.BeforeAlias,
                AfterIconPath = entry.BeforeIconPath,
                AfterIconIndex = entry.BeforeIconIndex,
            });
            _notify.Success(_loc["Common.Success"], entry.FolderName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从历史恢复失败 {p}", entry.FolderPath);
            _notify.Warning(_loc["Common.Failed"], ex.Message);
        }
        finally { IsBusy = false; }
    }
}

public sealed class HistoryItemViewModel : ObservableObject
{
    public HistoryEntry Entry { get; }
    private readonly ILocalizationService? _loc;

    public HistoryItemViewModel(HistoryEntry entry, ILocalizationService? loc = null)
    {
        Entry = entry;
        _loc = loc;
        if (_loc is System.ComponentModel.INotifyPropertyChanged inpc)
        {
            inpc.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ActionText));
        }
    }

    public string TimestampText => Entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    public string FolderName => Entry.FolderName;
    public string FolderPath => Entry.FolderPath;
    public string ActionText
    {
        get
        {
            var key = Entry.Action == HistoryAction.Restore ? "History.Action.Restore" : "History.Action.Modify";
            return _loc is null ? (Entry.Action == HistoryAction.Restore ? "Restore" : "Modify") : _loc[key];
        }
    }

    public string BeforeText => Format(Entry.BeforeAlias, Entry.BeforeIconPath, Entry.BeforeIconIndex);
    public string AfterText  => Format(Entry.AfterAlias,  Entry.AfterIconPath,  Entry.AfterIconIndex);

    private static string Format(string alias, string iconPath, int iconIndex)
    {
        var a = string.IsNullOrEmpty(alias) ? "—" : alias;
        if (string.IsNullOrEmpty(iconPath)) return a;
        return $"{a}  ·  {Path.GetFileName(iconPath)}({iconIndex})";
    }
}
