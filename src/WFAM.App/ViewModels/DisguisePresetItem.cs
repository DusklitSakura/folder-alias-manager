using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WFAM.App.Models;
using WFAM.App.Services;

namespace WFAM.App.ViewModels;

/// <summary>
/// 把 <see cref="DisguisePreset"/> 包成可绑定的视图模型，
/// 暴露根据当前语言实时更新的 <see cref="DisplayName"/>。
/// </summary>
public sealed class DisguisePresetItem : ObservableObject, IDisposable
{
    public DisguisePreset Preset { get; }
    public string Clsid => Preset.Clsid;
    public string Symbol => Preset.Symbol;
    public string NameKey => Preset.NameKey;

    private readonly ILocalizationService _loc;

    public DisguisePresetItem(DisguisePreset preset, ILocalizationService loc)
    {
        Preset = preset;
        _loc = loc;
        if (loc is INotifyPropertyChanged inpc)
            inpc.PropertyChanged += OnLocChanged;
    }

    public string DisplayName => _loc[Preset.NameKey];

    private void OnLocChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]" || e.PropertyName == "CurrentLanguage")
            OnPropertyChanged(nameof(DisplayName));
    }

    public void Dispose()
    {
        if (_loc is INotifyPropertyChanged inpc)
            inpc.PropertyChanged -= OnLocChanged;
    }
}
