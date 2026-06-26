using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Services;
using AttendancePayrollSystem.ViewModels;
using Microsoft.Win32;

namespace AttendancePayrollSystem.Views
{
    public partial class AdminDashboardView : UserControl
    {
        private readonly AppBrandingRepository _appBrandingRepository = new();
        private bool _hasLoaded;
        private bool _isRefreshing;

        public AdminDashboardView()
        {
            InitializeComponent();
        }

        public void RefreshBrandingVisuals()
        {
            _ = LoadBrandingAsync();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasLoaded)
            {
                return;
            }

            _hasLoaded = true;

            if (Window.GetWindow(this) is not MainWindow)
            {
                await RefreshDashboardAsync();
            }

            await LoadBrandingAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow owner)
            {
                await owner.RefreshRuntimeDataAsync();
                return;
            }

            await RefreshDashboardAsync();
        }

        private async void OpenEmployeeManagementTile_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow owner)
            {
                await owner.OpenEmployeeManagementAsync();
            }
        }

        private async void OpenLeaveRequestsTile_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow owner)
            {
                await owner.OpenLeaveRequestsAsync();
            }
        }

        private async void OpenPayrollTile_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow owner)
            {
                await owner.OpenPayrollLauncherAsync();
            }
        }

        private void BirthdayBoardTile_Click(object sender, RoutedEventArgs e)
        {
            var window = new BirthdayBoardWindow
            {
                Owner = Window.GetWindow(this)
            };

            window.ShowDialog();
        }

        private void LatestRecordsTile_Click(object sender, RoutedEventArgs e)
        {
            var window = new LatestAttendanceWindow
            {
                Owner = Window.GetWindow(this)
            };

            window.ShowDialog();
        }

        private void OpenDtrLedgerTile_Click(object sender, RoutedEventArgs e)
        {
            var window = new DtrLedgerWindow
            {
                Owner = Window.GetWindow(this)
            };

            window.ShowDialog();
        }

        private void LogoutTile_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow owner)
            {
                owner.LogoutToLogin();
            }
        }

        private async void BackupDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var modeDialog = new DatabaseBackupModeWindow
            {
                Owner = owner
            };

            if (modeDialog.ShowDialog() != true)
            {
                return;
            }

            var backupMode = modeDialog.SelectedMode;
            var dialog = new SaveFileDialog
            {
                Title = $"Export {backupMode} Database Backup",
                Filter = "SQL Backup (*.sql)|*.sql",
                DefaultExt = ".sql",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"attendance-{backupMode.ToString().ToLowerInvariant()}-backup-{DateTime.Now:yyyyMMdd-HHmmss}.sql"
            };

            if (dialog.ShowDialog(owner) != true)
            {
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var result = await Task.Run(() => DatabaseBackupService.ExportToSql(dialog.FileName, backupMode));

                var referenceLine = string.IsNullOrWhiteSpace(result.ReferenceBackupPath)
                    ? string.Empty
                    : $"\nReference backup: {result.ReferenceBackupPath}";
                var deletedRowsLine = result.DeletedRowCount == 0
                    ? string.Empty
                    : $"\nDeleted rows exported: {result.DeletedRowCount}";

                MessageBox.Show(
                    owner,
                    $"{result.Mode} backup completed successfully.\n\nFile: {result.FilePath}\nManifest: {result.ManifestPath}\nTables affected: {result.TableCount}\nRows exported: {result.RowCount}{deletedRowsLine}{referenceLine}\n\nKeep the manifest file beside the SQL file for future differential or incremental backups.",
                    "Backup Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    owner,
                    $"Failed to export the database backup.\n{ex.Message}",
                    "Backup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void UpdateBrandLogoButton_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            if (owner == null)
            {
                return;
            }

            try
            {
                if (!ProfileImageFilePicker.TryPick(owner, out var imageBytes, title: "Choose Logo Image", imageLabel: "Logo image"))
                {
                    return;
                }

                _appBrandingRepository.UpdateLogoImage(imageBytes);
                ApplyBranding(imageBytes);

                if (owner is MainWindow mainWindow)
                {
                    mainWindow.RefreshBranding();
                }

                MessageBox.Show(
                    owner,
                    "Logo updated successfully.",
                    "Branding",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    owner,
                    $"Failed to update the logo.\n{ex.Message}",
                    "Branding Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearBrandLogoButton_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            if (owner == null)
            {
                return;
            }

            try
            {
                _appBrandingRepository.UpdateLogoImage(null);
                ApplyBranding(null);

                if (owner is MainWindow mainWindow)
                {
                    mainWindow.RefreshBranding();
                }

                MessageBox.Show(
                    owner,
                    "Logo cleared successfully.",
                    "Branding",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    owner,
                    $"Failed to clear the logo.\n{ex.Message}",
                    "Branding Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task LoadBrandingAsync()
        {
            try
            {
                var branding = await Task.Run(() => _appBrandingRepository.GetBranding());
                ApplyBranding(branding.LogoImage);
            }
            catch
            {
                ApplyBranding(null);
            }
        }

        private async Task RefreshDashboardAsync()
        {
            if (_isRefreshing)
            {
                return;
            }

            try
            {
                _isRefreshing = true;
                Mouse.OverrideCursor = Cursors.Wait;
                if (DataContext is AdminDashboardViewModel viewModel)
                {
                    await viewModel.RefreshDashboardAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"Failed to refresh dashboard.\n{ex.Message}",
                    "Dashboard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isRefreshing = false;
            }
        }

        private void ApplyBranding(byte[]? logoImage)
        {
            BrandingVisualHelper.ApplyLogo(DashboardBrandLogoImage, DashboardBrandLogoFallbackPanel, logoImage);
            BrandingVisualHelper.ApplyLogo(DashboardBrandLogoPreviewImage, DashboardBrandLogoPreviewFallbackPanel, logoImage);
            ClearBrandLogoButton.IsEnabled = logoImage != null && logoImage.Length > 0;
        }
    }
}
