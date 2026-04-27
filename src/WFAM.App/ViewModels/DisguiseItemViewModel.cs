using CommunityToolkit.Mvvm.ComponentModel;
using WFAM.App.Models;
using WFAM.App.Services;

namespace WFAM.App.ViewModels;

/// <summary>
/// 文件夹伪装页内的单条记录。
/// </summary>
public partial class DisguiseItemViewModel : ObservableObject
{
    public string Path { get; }
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsDisguised))]
    private DisguiseState _state;

    [ObservableProperty] private DisguisePresetItem? _selectedPreset;

    private readonly ILocalizationService _loc;

    public DisguiseItemViewModel(string path, ILocalizationService loc, DisguiseState state)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));
        _loc = loc;
        _state = state;
    }

    public bool IsDisguised => State.IsDisguised;

    public string StatusText
    {
        get
        {
            if (!State.IsDisguised) return _loc["Disguise.Status.Normal"];
            if (State.MatchedPresetNameKey is not null)
                return string.Format(_loc["Disguise.Status.DisguisedAs"], _loc[State.MatchedPresetNameKey]);
            return string.Format(_loc["Disguise.Status.DisguisedUnknown"], State.Clsid ?? "?");
        }
    }
}
