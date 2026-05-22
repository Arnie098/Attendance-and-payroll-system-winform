using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem
{
    public partial class MainWindow : Window
    {
        private readonly AdminDashboardViewModel _dashboardViewModel;
        private readonly string _currentUsername;
        private bool _isRefreshingRuntimeData;

        public MainWindow(string currentUsername = "admin")
        {
            InitializeComponent();
            _currentUsername = string.IsNullOrWhiteSpace(currentUsername) ? "admin" : currentUsername.Trim();
            _dashboardViewModel = new AdminDashboardViewModel();
            AdminDashboardTab.DataContext = _dashboardViewModel;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            await RefreshRuntimeDataAsync(showSyncError: false);
        }

        public async Task RefreshRuntimeDataAsync(bool showSyncError = true)
        {
            if (_isRefreshingRuntimeData)
            {
                return;
            }

            try
            {
                _isRefreshingRuntimeData = true;
                Mouse.OverrideCursor = Cursors.Wait;
                await TrySynchronizeRuntimeAsync(showSyncError);
                var dashboardTask = _dashboardViewModel.RefreshDashboardAsync();
                RefreshBranding();
                await dashboardTask;
            }
            catch (Exception ex)
            {
                if (showSyncError)
                {
                    MessageBox.Show(
                        $"Failed to refresh runtime data.\n{ex.Message}",
                        "Refresh",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isRefreshingRuntimeData = false;
            }
        }

        public void RefreshRuntimeData()
        {
            _ = RefreshRuntimeDataAsync();
        }

        public void RefreshBranding()
        {
            AdminDashboardTab.RefreshBrandingVisuals();
        }

        public async Task OpenEmployeeManagementAsync()
        {
            var window = new EmployeeManagementWindow
            {
                Owner = this
            };

            window.ShowDialog();
            await RefreshRuntimeDataAsync(showSyncError: false);
        }

        public async Task OpenLeaveRequestsAsync()
        {
            var window = new LeaveRequestsWindow(_currentUsername)
            {
                Owner = this
            };

            window.ShowDialog();
            await RefreshRuntimeDataAsync(showSyncError: false);
        }

        public async Task OpenPayrollLauncherAsync()
        {
            var window = new PayrollLauncherWindow
            {
                Owner = this
            };

            window.ShowDialog();
            await RefreshRuntimeDataAsync(showSyncError: false);
        }

        public void LogoutToLogin()
        {
            Logout_Click(this, new RoutedEventArgs());
        }

        private async Task TrySynchronizeRuntimeAsync(bool showSyncError = true)
        {
            try
            {
                await Task.Run(() => Services.MySqlOfflineSyncService.TrySynchronizeNow());
            }
            catch (Exception ex)
            {
                if (!showSyncError)
                {
                    return;
                }

                MessageBox.Show(
                    $"Database synchronization could not be completed.\n{ex.Message}",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new LoginWindow();
            Application.Current.MainWindow = loginWindow;
            loginWindow.Show();
            Close();
        }
    }
}
