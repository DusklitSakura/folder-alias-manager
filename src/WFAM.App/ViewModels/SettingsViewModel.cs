using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using WFAM.App.Models;
using WFAM.App.Services;

namespace WFAM.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly IContextMenuService _contextMenu;
    private readonly IThemeManager _themeManager;
    private readonly INotificationService _notify;
    private readonly IUpdateService? _updates;
    private readonly IUpdatePromptService? _prompt;
    private readonly ILogger<SettingsViewModel> _logger;
    private bool _suppressPersist;
    private UpdateInfo? _pendingUpdate;

    public SettingsViewModel(
        ISettingsService settings,
        ILocalizationService localization,
        IContextMenuService contextMenu,
        IThemeManager themeManager,
        INotificationService notify,
        AboutViewModel about,
        IUpdateService updates,
        IUpdatePromptService prompt,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _localization = localization;
        _contextMenu = contextMenu;
        _themeManager = themeManager;
        _notify = notify;
        _updates = updates;
        _prompt = prompt;
        _logger = logger;
        About = about;

        AvailableThemes = new[]
        {
            new ThemeOption(ThemeMode.System,       "Settings.Theme.System",       _localization),
            new ThemeOption(ThemeMode.Light,        "Settings.Theme.Light",        _localization),
            new ThemeOption(ThemeMode.Dark,         "Settings.Theme.Dark",         _localization),
            new ThemeOption(ThemeMode.HighContrast, "Settings.Theme.HighContrast", _localization),
        };
        AvailableLanguages = localization.AvailableLanguages;

        _suppressPersist = true;
        try
        {
            var mode = ParseThemeMode(_settings.Current.Theme);
            CurrentTheme = AvailableThemes.First(t => t.Mode == mode);
            CurrentLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == _settings.Current.Language)
                              ?? AvailableLanguages[0];
            IsContextMenuRegistered = _contextMenu.IsRegistered;
            AutoCheckUpdate = _settings.Current.AutoCheckUpdate;
        }
        finally { _suppressPersist = false; }

        CurrentVersionText = _updates?.CurrentVersion.ToString(3) ?? "";
    }

    // 设计期默认构造
    public SettingsViewModel() : this(null!, null!, null!, null!, null!, new AboutViewModel(), null!, null!, null!) { }

    // ---------- 关于 ----------
    public AboutViewModel About { get; }

    // ---------- 主题 ----------
    public IReadOnlyList<ThemeOption> AvailableThemes { get; }

    [ObservableProperty] private ThemeOption _currentTheme = default!;

    partial void OnCurrentThemeChanged(ThemeOption value)
    {
        if (value is null) return;
        _themeManager.Apply(value.Mode);
        if (_suppressPersist) return;
        _settings.Current.Theme = value.Mode.ToString();
        _settings.Save();
    }

    // ---------- 语言 ----------
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    [ObservableProperty] private LanguageOption _currentLanguage = new("zh-CN", "简体中文");

    partial void OnCurrentLanguageChanged(LanguageOption value)
    {
        _localization.SetLanguage(value.Code);
        if (_suppressPersist) return;
        _settings.Current.Language = value.Code;
        _settings.Save();
    }

    // ---------- 右键菜单 ----------
    [ObservableProperty] private bool _isContextMenuRegistered;

    [RelayCommand]
    private void RegisterContextMenu()
    {
        try
        {
            var label = _localization["Settings.ContextMenu.MenuLabel"];
            _contextMenu.Register(label);
            IsContextMenuRegistered = true;
            _notify.Success(_localization["Common.Success"], _localization["Notify.ContextMenu.Registered"]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册右键菜单失败");
            _notify.Warning(_localization["Common.Failed"],
                string.Format(_localization["Notify.ContextMenu.Failed"], ex.Message));
        }
    }

    [RelayCommand]
    private void UnregisterContextMenu()
    {
        try
        {
            _contextMenu.Unregister();
            IsContextMenuRegistered = false;
            _notify.Success(_localization["Common.Success"], _localization["Notify.ContextMenu.Unregistered"]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消右键菜单失败");
            _notify.Warning(_localization["Common.Failed"],
                string.Format(_localization["Notify.ContextMenu.Failed"], ex.Message));
        }
    }

    private static ThemeMode ParseThemeMode(string s)
    {
        if (Enum.TryParse<ThemeMode>(s, ignoreCase: true, out var m)) return m;
        // 兼容旧设置中保存的 "Light"/"Dark"/"HighContrast" 以及其它值
        return ThemeMode.System;
    }

    // ---------- 更新 ----------
    public string CurrentVersionText { get; }

    [ObservableProperty] private bool _autoCheckUpdate;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private string _updateStatusText = string.Empty;
    [ObservableProperty] private bool _hasUpdateAvailable;
    [ObservableProperty] private string _latestVersionText = string.Empty;

    public bool CanCheckUpdate => !IsCheckingUpdate;
    partial void OnIsCheckingUpdateChanged(bool value) => OnPropertyChanged(nameof(CanCheckUpdate));

    partial void OnAutoCheckUpdateChanged(bool value)
    {
        if (_suppressPersist) return;
        _settings.Current.AutoCheckUpdate = value;
        _settings.Save();
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (_updates is null || IsCheckingUpdate) return;
        IsCheckingUpdate = true;
        UpdateStatusText = _localization["Update.Checking"];
        HasUpdateAvailable = false;
        try
        {
            // 手动检查：不过滤 LastSkipped，允许用户主动看到被忽略过的版本
            var info = await _updates.CheckAsync(onlyIfNewer: false);
            if (info is null)
            {
                UpdateStatusText = _localization["Update.CheckFailed"];
                return;
            }
            _pendingUpdate = info;
            if (info.Version > _updates.CurrentVersion)
            {
                HasUpdateAvailable = true;
                LatestVersionText = info.Version.ToString(3);
                UpdateStatusText = string.Format(_localization["Update.Available.Format"], LatestVersionText);
                // 手动检查发现新版本 → 直接弹出发现更新对话框
                if (_prompt is not null)
                    await _prompt.PromptAsync(info);
            }
            else
            {
                UpdateStatusText = _localization["Update.UpToDate"];
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "检查更新失败");
            UpdateStatusText = _localization["Update.CheckFailed"];
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_pendingUpdate is null || _prompt is null) return;
        await _prompt.PromptAsync(_pendingUpdate);
    }

    [RelayCommand]
    private void SkipThisVersion()
    {
        if (_pendingUpdate is null) return;
        _settings.Current.LastSkippedUpdateVersion = _pendingUpdate.Version.ToString();
        _settings.Save();
        HasUpdateAvailable = false;
        UpdateStatusText = _localization["Update.Skipped"];
    }
}
