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
            if (DataContext is AdminDashboardViewModel viewModel)
            {
                viewModel.RefreshDashboard();
            }
        }

        private async void BackupDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var dialog = new SaveFileDialog
            {
                Title = "Export Database Backup",
                Filter = "SQL Backup (*.sql)|*.sql",
                DefaultExt = ".sql",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"attendance-backup-{DateTime.Now:yyyyMMdd-HHmmss}.sql"
            };

            if (dialog.ShowDialog(owner) != true)
            {
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var result = await Task.Run(() => DatabaseBackupService.ExportToSql(dialog.FileName));

                MessageBox.Show(
                    owner,
                    $"Backup completed successfully.\n\nFile: {result.FilePath}\nTables exported: {result.TableCount}\nRows exported: {result.RowCount}\n\nThis SQL file is ready to import into another MySQL server.",
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
