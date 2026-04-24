using System.Windows.Controls;
using WFAM.App.ViewModels;

namespace WFAM.App.Views.Pages;

public partial class UsbDrivesPage : Page
{
    private readonly UsbDrivesViewModel _vm;

    public UsbDrivesPage(UsbDrivesViewModel viewModel)
    {
        DataContext = _vm = viewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (!_vm.HasLoaded)
                await _vm.RefreshCommand.ExecuteAsync(null);
        };
    }
}
