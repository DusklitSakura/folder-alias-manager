using System.Windows.Controls;
using WFAM.App.ViewModels;

namespace WFAM.App.Views.Pages;

public partial class FoldersPage : Page
{
    public FoldersPage(FoldersViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
