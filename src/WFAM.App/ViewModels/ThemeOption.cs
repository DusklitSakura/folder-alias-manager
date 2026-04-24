using System.ComponentModel;
using WFAM.App.Models;

namespace WFAM.App.ViewModels;

/// <summary>
/// 主题下拉项：自身实现 INotifyPropertyChanged，
/// 当语言切换时刷新 DisplayName，使 ComboBox 项显示自动随之更新。
/// </summary>
public sealed class ThemeOption : INotifyPropertyChanged
{
    private readonly Services.ILocalizationService _loc;
    private readonly string _key;

    public ThemeMode Mode { get; }
    public string DisplayName => _loc[_key];

    public event PropertyChangedEventHandler? PropertyChanged;

    public ThemeOption(ThemeMode mode, string key, Services.ILocalizationService loc)
    {
        Mode = mode;
        _key = key;
        _loc = loc;
        _loc.PropertyChanged += OnLocChanged;
    }

    private void OnLocChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]" || e.PropertyName == nameof(Services.ILocalizationService.CurrentLanguage))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }

    public override bool Equals(object? obj) => obj is ThemeOption o && o.Mode == Mode;
    public override int GetHashCode() => Mode.GetHashCode();
}
