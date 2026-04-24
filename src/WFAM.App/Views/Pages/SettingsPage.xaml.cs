using System.Windows.Controls;
using WFAM.App.ViewModels;

namespace WFAM.App.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
