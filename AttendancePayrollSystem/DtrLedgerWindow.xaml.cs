using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using Microsoft.Win32;

namespace AttendancePayrollSystem
{
    public partial class DtrLedgerWindow : Window
    {
        private readonly AttendanceRepository _repo = new();
        private List<DtrLedgerEntry> _allEntries = new();
        private bool _isLoading;
        private bool _suppressFilters;
        private DispatcherTimer? _searchDebounce;

        public DtrLedgerWindow()
        {
            InitializeComponent();
            Loaded += DtrLedgerWindow_Loaded;
        }

        private async void DtrLedgerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= DtrLedgerWindow_Loaded;

            // Suppress filter events while setting up controls so we don't
            // fire multiple overlapping loads before the window is ready.
            _suppressFilters = true;
            await LoadDepartmentsAsync();
            StartDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            EndDatePicker.SelectedDate = DateTime.Today;
            _suppressFilters = false;

            await LoadLedgerAsync();
        }

        // ── Department dropdown ───────────────────────────────────────────────

        private async Task LoadDepartmentsAsync()
        {
            try
            {
                // GetDistinctDepartments() already prepends "All" at index 0.
                var departments = await Task.Run(() => _repo.GetDistinctDepartments());
                DeptFilterCombo.Items.Clear();
                foreach (var dept in departments)
                    DeptFilterCombo.Items.Add(new ComboBoxItem { Content = dept });
                DeptFilterCombo.SelectedIndex = 0;
            }
            catch
            {
                DeptFilterCombo.Items.Clear();
                DeptFilterCombo.Items.Add(new ComboBoxItem { Content = "All" });
                DeptFilterCombo.SelectedIndex = 0;
            }
        }

        // ── Data loading ──────────────────────────────────────────────────────

        private async Task LoadLedgerAsync()
        {
            if (_isLoading) return;
            try
            {
                _isLoading = true;
                Mouse.OverrideCursor = Cursors.Wait;

                var query = BuildQuery();
                _allEntries = await Task.Run(() => _repo.GetDtrLedger(query));

                BindGrid();
                LastLoadedText.Text = $"Loaded: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load DTR ledger.\n{ex.Message}",
                    "DTR Ledger",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isLoading = false;
            }
        }

        private DtrLedgerQuery BuildQuery()
        {
            return new DtrLedgerQuery
            {
                SearchText       = SearchBox.Text?.Trim() ?? string.Empty,
                Department       = (DeptFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All",
                Status           = (StatusFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All",
                StartDate        = StartDatePicker.SelectedDate,
                EndDate          = EndDatePicker.SelectedDate,
                MissingPunchOnly = MissingPunchCheck.IsChecked == true,
                Limit            = 1000
            };
        }

        // All filtering is done server-side. BindGrid just attaches the result.
        private void BindGrid()
        {
            LedgerGrid.ItemsSource = _allEntries;
            UpdateSummary(_allEntries);
        }

        private void UpdateSummary(List<DtrLedgerEntry> entries)
        {
            BadgeRecordsText.Text  = $"{entries.Count} records";
            BadgePresentText.Text  = $"{entries.Count(e => string.Equals(e.Status, "Present", StringComparison.OrdinalIgnoreCase))} Present";
            BadgeLateText.Text     = $"{entries.Count(e => string.Equals(e.Status, "Late",    StringComparison.OrdinalIgnoreCase))} Late";
            BadgeLeaveText.Text    = $"{entries.Count(e => e.Status.Contains("Leave",          StringComparison.OrdinalIgnoreCase))} Leave";
            BadgeMissingText.Text  = $"{entries.Count(e => e.HasMissingPunch)} Missing Punch";

            var totalHours     = Math.Round(entries.Sum(e => e.TotalHours), 2);
            var totalTardiness = entries.Sum(e => e.TardinessMinutes);
            TotalsText.Text    = $"Total: {totalHours:N2} hrs  |  Tardiness: {totalTardiness} min";

            FilteredCountText.Text = $"Showing {entries.Count} records";
        }

        // ── Filter event handlers ─────────────────────────────────────────────

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || _suppressFilters) return;

            // Debounce: wait 350 ms after the user stops typing before querying.
            _searchDebounce?.Stop();
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _searchDebounce.Tick += async (_, _) =>
            {
                _searchDebounce?.Stop();
                _searchDebounce = null;
                await LoadLedgerAsync();
            };
            _searchDebounce.Start();
        }

        private async void Filter_Changed(object sender, EventArgs e)
        {
            if (!IsLoaded || _suppressFilters) return;
            await LoadLedgerAsync();
        }

        private async void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            // Suppress individual filter-change events so we do exactly one load.
            _suppressFilters = true;
            _searchDebounce?.Stop();
            _searchDebounce = null;

            SearchBox.Text = string.Empty;
            if (DeptFilterCombo.Items.Count > 0) DeptFilterCombo.SelectedIndex = 0;
            StatusFilterCombo.SelectedIndex = 0;
            StartDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            EndDatePicker.SelectedDate = DateTime.Today;
            MissingPunchCheck.IsChecked = false;

            _suppressFilters = false;
            await LoadLedgerAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadLedgerAsync();
        }

        // ── CSV export ────────────────────────────────────────────────────────

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_allEntries.Count == 0)
            {
                MessageBox.Show("No records to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter     = "CSV Files (*.csv)|*.csv",
                FileName   = $"DTR_Ledger_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Code,Full Name,Department,Date,AM In,AM Out,PM In,PM Out,AM In Status,AM Out Status,PM In Status,PM Out Status,Hours,Late (min),Biometric,Missing Punch");
                foreach (var row in _allEntries)
                {
                    sb.AppendLine(string.Join(",",
                        Csv(row.EmployeeCode),
                        Csv(row.FullName),
                        Csv(row.Department),
                        row.AttendanceDate.ToString("yyyy-MM-dd"),
                        row.TimeInAM?.ToString("hh:mm tt")  ?? string.Empty,
                        row.TimeOutAM?.ToString("hh:mm tt") ?? string.Empty,
                        row.TimeInPM?.ToString("hh:mm tt")  ?? string.Empty,
                        row.TimeOutPM?.ToString("hh:mm tt") ?? string.Empty,
                        Csv(row.TimeInAMStatus),
                        Csv(row.TimeOutAMStatus),
                        Csv(row.TimeInPMStatus),
                        Csv(row.TimeOutPMStatus),
                        row.TotalHours.ToString("N2"),
                        row.TardinessMinutes.ToString(),
                        row.IsBiometricVerified ? "Yes" : "No",
                        row.HasMissingPunch ? "Yes" : "No"));
                }
                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show(
                    $"Exported {_allEntries.Count} records to:\n{dialog.FileName}",
                    "Export Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export CSV.\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Contains(',') || value.Contains('"') || value.Contains('\n')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }
    }
}
