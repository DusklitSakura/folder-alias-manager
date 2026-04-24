using System.Windows.Controls;
using WFAM.App.ViewModels;

namespace WFAM.App.Views.Pages;

public partial class HistoryPage : Page
{
    public HistoryPage(HistoryViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
