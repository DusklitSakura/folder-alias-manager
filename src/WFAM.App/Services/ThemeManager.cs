using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using ThemeMode = WFAM.App.Models.ThemeMode;

namespace WFAM.App.Services;

public interface IThemeManager
{
    /// <summary>应用主题模式（System 时跟随系统并监听变更）。</summary>
    void Apply(ThemeMode mode);
}

public sealed class ThemeManager : IThemeManager, IDisposable
{
    private bool _watchingSystem;

    public void Apply(ThemeMode mode)
    {
        if (mode == ThemeMode.System)
        {
            ApplyTheme(GetCurrentSystemTheme());
            EnableSystemWatcher();
        }
        else
        {
            DisableSystemWatcher();
            ApplyTheme(mode switch
            {
                ThemeMode.Light => ApplicationTheme.Light,
                ThemeMode.Dark => ApplicationTheme.Dark,
                ThemeMode.HighContrast => ApplicationTheme.HighContrast,
                _ => ApplicationTheme.Dark,
            });
        }
    }

    private static void ApplyTheme(ApplicationTheme theme) => ApplicationThemeManager.Apply(theme);

    private void EnableSystemWatcher()
    {
        if (_watchingSystem) return;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _watchingSystem = true;
    }

    private void DisableSystemWatcher()
    {
        if (!_watchingSystem) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _watchingSystem = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        // SystemEvents 在非 UI 线程触发，必须切回 UI 线程
        Application.Current?.Dispatcher.Invoke(() => ApplyTheme(GetCurrentSystemTheme()));
    }

    private static ApplicationTheme GetCurrentSystemTheme()
    {
        // Windows: HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 0 ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }
        catch
        {
            // ignore
        }
        return ApplicationTheme.Dark;
    }

    public void Dispose() => DisableSystemWatcher();
}
