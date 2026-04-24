using System.Windows.Controls;
using WFAM.App.ViewModels;

namespace WFAM.App.Views.Pages;

public partial class AboutPage : Page
{
    public AboutPage(AboutViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
