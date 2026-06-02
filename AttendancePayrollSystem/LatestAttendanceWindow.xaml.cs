using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AttendancePayrollSystem.ViewModels;
using Microsoft.Win32;

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
                UpdateSummaryBadges();
                ApplyFilters();
                LastRefreshedText.Text = $"Last refreshed: {DateTime.Now:HH:mm:ss}";
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

        private void UpdateSummaryBadges()
        {
            var items = _viewModel.LatestAttendances;
            var presentCount = items.Count(i => string.Equals(i.Status, "Present", StringComparison.OrdinalIgnoreCase));
            var lateCount = items.Count(i => string.Equals(i.Status, "Late", StringComparison.OrdinalIgnoreCase));

            PresentCountText.Text = $"{presentCount} Present";
            LateCountText.Text = $"{lateCount} Late";
            TotalCountText.Text = $"{items.Count} records";
        }

        private void ApplyFilters()
        {
            var view = CollectionViewSource.GetDefaultView(_viewModel.LatestAttendances);
            if (view == null) return;

            var searchText = SearchBox.Text?.Trim() ?? string.Empty;
            var statusFilter = (StatusFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            var dateFilter = DateFilterPicker.SelectedDate;

            view.Filter = obj =>
            {
                if (obj is not LatestAttendanceItem item) return false;

                // Status filter
                if (!string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(item.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Date filter
                if (dateFilter.HasValue)
                {
                    if (item.AttendanceDate.Date != dateFilter.Value.Date)
                        return false;
                }

                // Search filter
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var match = item.EmployeeCode.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                item.FullName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                item.Status.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                item.AttendanceDate.ToString("yyyy-MM-dd").Contains(searchText, StringComparison.OrdinalIgnoreCase);
                    if (!match) return false;
                }

                return true;
            };

            // Update filtered count text
            var filteredCount = view.Cast<object>().Count();
            var totalCount = _viewModel.LatestAttendances.Count;
            FilteredCountText.Text = filteredCount == totalCount
                ? $"Showing all {totalCount} records"
                : $"Showing {filteredCount} of {totalCount} records";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void StatusFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Guard against early calls before window is fully loaded
            if (!IsLoaded) return;
            ApplyFilters();
        }

        private void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyFilters();
        }

        private void ClearDateFilter_Click(object sender, RoutedEventArgs e)
        {
            DateFilterPicker.SelectedDate = null;
            ApplyFilters();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var view = CollectionViewSource.GetDefaultView(_viewModel.LatestAttendances);
            var items = view?.Cast<LatestAttendanceItem>().ToList() ?? _viewModel.LatestAttendances.ToList();

            if (items.Count == 0)
            {
                MessageBox.Show("No records to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"Attendance_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Employee Code,Full Name,Date,In AM,Out AM,In PM,Out PM,Total Hours,Late (min),Status");

                foreach (var item in items)
                {
                    sb.AppendLine(string.Join(",",
                        EscapeCsv(item.EmployeeCode),
                        EscapeCsv(item.FullName),
                        item.AttendanceDate.ToString("yyyy-MM-dd"),
                        item.TimeIn?.ToString("HH:mm") ?? "",
                        item.TimeOutAM?.ToString("HH:mm") ?? "",
                        item.TimeInPM?.ToString("HH:mm") ?? "",
                        item.TimeOut?.ToString("HH:mm") ?? "",
                        item.TotalHours.ToString("N2"),
                        item.TardinessMinutes.ToString(),
                        EscapeCsv(item.Status)));
                }

                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show(
                    $"Exported {items.Count} records to:\n{dialog.FileName}",
                    "Export Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to export CSV.\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
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
