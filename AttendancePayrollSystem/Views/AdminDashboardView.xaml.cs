using System.Windows;
using System.Windows.Controls;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem.Views
{
    public partial class AdminDashboardView : UserControl
    {
        public AdminDashboardView()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AdminDashboardViewModel viewModel)
            {
                viewModel.RefreshDashboard();
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AdminDashboardViewModel viewModel)
            {
                viewModel.RefreshDashboard();
            }
        }
    }
}
