using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem
{
    public partial class LatestAttendanceWindow : Window
    {
        private readonly AdminDashboardViewModel _viewModel = new();
        private bool _isRefreshing;

        public LatestAttendanceWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            Loaded += LatestAttendanceWindow_Loaded;
        }

        private async void LatestAttendanceWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= LatestAttendanceWindow_Loaded;
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (_isRefreshing)
            {
                return;
            }

            try
            {
                _isRefreshing = true;
                Mouse.OverrideCursor = Cursors.Wait;
                await _viewModel.RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load latest attendance records.\n{ex.Message}",
                    "Latest Attendance",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isRefreshing = false;
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
