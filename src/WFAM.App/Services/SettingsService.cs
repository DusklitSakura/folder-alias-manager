using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WFAM.App.Models;

namespace WFAM.App.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private readonly ILogger<SettingsService> _logger;
    private readonly string _path;

    public AppSettings Current { get; private set; } = new();

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WFAM");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var text = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(text, JsonOpts);
            if (loaded is not null) Current = loaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取设置失败，使用默认值。");
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var text = JsonSerializer.Serialize(Current, JsonOpts);
            File.WriteAllText(_path, text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入设置失败：{path}", _path);
        }
    }
}
