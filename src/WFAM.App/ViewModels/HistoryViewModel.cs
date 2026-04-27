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
    private readonly IFolderDisguiseService _disguise;
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
        IFolderDisguiseService disguise,
        IElevationService elevation,
        IShellService shell,
        INotificationService notify,
        ILocalizationService loc,
        ILogger<HistoryViewModel> logger)
    {
        _history = history; _ini = ini; _disguise = disguise; _elevation = elevation;
        _shell = shell; _notify = notify; _loc = loc; _logger = logger;
        _history.Changed += (_, _) => Reload();
        Reload();
    }

    public HistoryViewModel() : this(null!, null!, null!, null!, null!, null!, null!, null!) { }

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
            // 如果这是伪装记录，走伪装服务 + Helper
            var isDisguiseEntry = !string.Equals(entry.BeforeClsid, entry.AfterClsid, StringComparison.OrdinalIgnoreCase);
            if (isDisguiseEntry)
            {
                await RestoreDisguiseAsync(entry);
                return;
            }

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
                var rs = await _ini.WriteAsync(entry.FolderPath, entry.BeforeAlias, iconPath, entry.BeforeIconIndex, null);
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

    private async Task RestoreDisguiseAsync(HistoryEntry entry)
    {
        var currentState = _disguise.Detect(entry.FolderPath);
        var targetClsid = entry.BeforeClsid; // 还原到伪装前状态

        WriteOutcome outcome;
        string? failMessage;
        if (string.IsNullOrEmpty(targetClsid))
        {
            var rs = await _disguise.RestoreAsync(entry.FolderPath);
            outcome = rs.Outcome; failMessage = rs.Message;
            if (outcome == WriteOutcome.AccessDenied && _elevation.IsHelperAvailable)
            {
                var r = await _elevation.ElevatedBatchDisguiseAsync(new[]
                {
                    new ElevatedDisguiseRequest(entry.FolderPath, entry.FolderName, string.Empty, Restore: true),
                });
                if (r.Count > 0) { outcome = r[0].Outcome; failMessage = r[0].Message; }
                else { outcome = WriteOutcome.Failed; failMessage = "Helper 未返回结果"; }
            }
        }
        else
        {
            var rs = await _disguise.DisguiseAsync(entry.FolderPath, targetClsid);
            outcome = rs.Outcome; failMessage = rs.Message;
            if (outcome == WriteOutcome.AccessDenied && _elevation.IsHelperAvailable)
            {
                var r = await _elevation.ElevatedBatchDisguiseAsync(new[]
                {
                    new ElevatedDisguiseRequest(entry.FolderPath, entry.FolderName, targetClsid),
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
            BeforeClsid = currentState.Clsid ?? string.Empty,
            AfterClsid = targetClsid,
        });
        _notify.Success(_loc["Common.Success"], entry.FolderName);
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

    public string BeforeText => Format(Entry.BeforeAlias, Entry.BeforeIconPath, Entry.BeforeIconIndex, Entry.BeforeClsid);
    public string AfterText  => Format(Entry.AfterAlias,  Entry.AfterIconPath,  Entry.AfterIconIndex,  Entry.AfterClsid);

    private string Format(string alias, string iconPath, int iconIndex, string clsid)
    {
        // 伪装记录优先显示“伪装: <预设名称> / CLSID”
        if (!string.IsNullOrEmpty(clsid))
        {
            var label = LookupPresetName(clsid) ?? clsid;
            var prefix = _loc is null ? "Disguise" : _loc["Disguise.Title"];
            return $"{prefix}: {label}";
        }
        var a = string.IsNullOrEmpty(alias) ? "—" : alias;
        if (string.IsNullOrEmpty(iconPath)) return a;
        return $"{a}  ·  {Path.GetFileName(iconPath)}({iconIndex})";
    }

    private string? LookupPresetName(string clsid)
    {
        var disguise = App.Services?.GetService(typeof(IFolderDisguiseService)) as IFolderDisguiseService;
        var preset = disguise?.Presets.FirstOrDefault(p =>
            string.Equals(p.Clsid, clsid, StringComparison.OrdinalIgnoreCase));
        if (preset is null) return null;
        return _loc is null ? preset.NameKey : _loc[preset.NameKey];
    }
}
