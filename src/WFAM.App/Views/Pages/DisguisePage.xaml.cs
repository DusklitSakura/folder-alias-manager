using System.Windows.Controls;
using WFAM.App.ViewModels;

namespace WFAM.App.Views.Pages;

public partial class DisguisePage : Page
{
    public DisguisePage(DisguiseViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
