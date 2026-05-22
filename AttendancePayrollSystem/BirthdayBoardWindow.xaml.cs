using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem
{
    public partial class BirthdayBoardWindow : Window
    {
        private readonly AdminDashboardViewModel _viewModel = new();
        private bool _isRefreshing;

        public BirthdayBoardWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            Loaded += BirthdayBoardWindow_Loaded;
        }

        private async void BirthdayBoardWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= BirthdayBoardWindow_Loaded;
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
                    $"Failed to load birthday board.\n{ex.Message}",
                    "Birthday Board",
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
