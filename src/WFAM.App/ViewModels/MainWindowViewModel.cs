using CommunityToolkit.Mvvm.ComponentModel;

namespace WFAM.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "WFAM · 文件夹别名管理器";
}
