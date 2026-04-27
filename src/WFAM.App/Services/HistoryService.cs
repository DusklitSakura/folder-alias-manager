using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WFAM.App.Models;

namespace WFAM.App.Services;

public sealed class HistoryService : IHistoryService
{
    private const int MaxEntries = 200;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ILogger<HistoryService> _logger;
    private readonly string _path;
    private readonly List<HistoryEntry> _entries = new();
    private readonly object _gate = new();

    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    public event EventHandler? Changed;

    public HistoryService(ILogger<HistoryService> logger)
    {
        _logger = logger;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WFAM");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "history.json");
        Load();
    }

    public void Add(HistoryEntry entry)
    {
        if (entry is null) return;
        // 跳过没有实际改动的条目
        if (string.Equals(entry.BeforeAlias, entry.AfterAlias, StringComparison.Ordinal)
            && string.Equals(entry.BeforeIconPath, entry.AfterIconPath, StringComparison.OrdinalIgnoreCase)
            && entry.BeforeIconIndex == entry.AfterIconIndex
            && string.Equals(entry.BeforeClsid, entry.AfterClsid, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_gate)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _entries.RemoveAll(e => e.Id == id) > 0;
            if (removed) Save();
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_entries.Count == 0) return;
            _entries.Clear();
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var text = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(text, JsonOpts);
            if (loaded is not null)
            {
                _entries.Clear();
                _entries.AddRange(loaded.OrderByDescending(e => e.Timestamp));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取历史记录失败");
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries, JsonOpts));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入历史记录失败：{path}", _path);
        }
    }
}
