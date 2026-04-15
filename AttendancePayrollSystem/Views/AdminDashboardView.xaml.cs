using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.ViewModels;
using Microsoft.Win32;

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
            if (Window.GetWindow(this) is MainWindow owner)
            {
                owner.RefreshRuntimeData();
                return;
            }

            if (DataContext is AdminDashboardViewModel viewModel)
            {
                viewModel.RefreshDashboard();
            }
        }

        private void OpenEmployeeManagementTile_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow owner)
            {
                owner.NavigateToEmployeeManagement();
            }
        }

        private void BirthdayBoardTile_Click(object sender, RoutedEventArgs e)
        {
            BirthdayPanel.BringIntoView();
        }

        private void LatestRecordsTile_Click(object sender, RoutedEventArgs e)
        {
            LatestAttendancePanel.BringIntoView();
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
    }
}
