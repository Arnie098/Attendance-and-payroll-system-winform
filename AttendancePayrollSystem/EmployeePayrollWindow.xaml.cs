using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using Microsoft.Win32;

namespace AttendancePayrollSystem
{
    public partial class EmployeePayrollWindow : Window
    {
        private readonly Employee _employee;
        private readonly PayrollRepository _payrollRepository = new();
        private List<Payroll> _allPayrolls = new();

        public EmployeePayrollWindow(Employee employee)
        {
            InitializeComponent();
            _employee = employee;
            EmployeeSubtitleText.Text = $"{employee.EmployeeCode} — {employee.FullName}";
            Loaded += EmployeePayrollWindow_Loaded;
        }

        private void EmployeePayrollWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= EmployeePayrollWindow_Loaded;
            LoadPayrolls();
        }

        private void LoadPayrolls()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                _allPayrolls = _payrollRepository.GetPayrollByEmployee(_employee.EmployeeId);
                ApplyFilters();
                UpdateSummaryBadges();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load payroll records.\n{ex.Message}",
                    "Payroll",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void UpdateSummaryBadges()
        {
            var totalNet = _allPayrolls.Sum(p => p.NetPay);
            TotalNetPayText.Text = $"Net Total: ₱{totalNet:N2}";
            RecordCountText.Text = $"{_allPayrolls.Count} records";
        }

        private void ApplyFilters()
        {
            var searchText = SearchBox.Text?.Trim() ?? string.Empty;
            var statusFilter = (StatusFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";

            var filtered = _allPayrolls.AsEnumerable();

            if (!string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(p => string.Equals(p.Status, statusFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filtered = filtered.Where(p =>
                    p.PayPeriodStart.ToString("yyyy-MM-dd").Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.PayPeriodEnd.ToString("yyyy-MM-dd").Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.Status.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.NetPay.ToString("N2").Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.GrossPay.ToString("N2").Contains(searchText, StringComparison.OrdinalIgnoreCase));
            }

            var result = filtered.ToList();
            PayrollGrid.ItemsSource = result;

            var totalCount = _allPayrolls.Count;
            FilteredCountText.Text = result.Count == totalCount
                ? $"Showing all {totalCount} records"
                : $"Showing {result.Count} of {totalCount} records";

            // Hide payslip panel when filters change
            PayslipPanel.Visibility = Visibility.Collapsed;
        }

        private void PayrollGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PayrollGrid.SelectedItem is not Payroll selected)
            {
                PayslipPanel.Visibility = Visibility.Collapsed;
                HintText.Text = "Click a row to see payslip details.";
                return;
            }

            ShowPayslipDetail(selected);
        }

        private void ShowPayslipDetail(Payroll p)
        {
            DetailPeriod.Text = $"{p.PayPeriodStart:MMM dd} – {p.PayPeriodEnd:MMM dd, yyyy}";
            DetailStatus.Text = p.Status;

            DetailRegular.Text = $"Regular: {p.RegularHours:N2} hrs × rate = ₱{p.GrossPay - (p.OvertimeHours > 0 ? p.OvertimeHours * (_employee.HourlyRate * 1.25m) : 0):N2}";
            DetailOvertime.Text = p.OvertimeHours > 0
                ? $"Overtime: {p.OvertimeHours:N2} hrs (×1.25)"
                : "Overtime: none";
            DetailGross.Text = $"Gross Pay: ₱{p.GrossPay:N2}";

            DetailLate.Text = p.TotalTardinessMinutes > 0
                ? $"Tardiness: {p.TotalTardinessMinutes} min (−₱{p.TardinessDeduction:N2})"
                : "Tardiness: none";
            DetailManual.Text = p.ManualDeduction > 0
                ? $"Manual Ded: −₱{p.ManualDeduction:N2}"
                : "Manual Ded: none";
            DetailTotalDed.Text = $"Total Deductions: −₱{p.Deductions:N2}";

            DetailNetPay.Text = $"₱{p.NetPay:N2}";
            DetailManualNote.Text = !string.IsNullOrWhiteSpace(p.ManualDeductionNote)
                ? $"Note: {p.ManualDeductionNote}"
                : string.Empty;

            PayslipPanel.Visibility = Visibility.Visible;
            HintText.Text = $"Showing details for {p.PayPeriodStart:yyyy-MM-dd} – {p.PayPeriodEnd:yyyy-MM-dd}";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void StatusFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyFilters();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var items = (PayrollGrid.ItemsSource as IEnumerable<Payroll>)?.ToList() ?? _allPayrolls;

            if (items.Count == 0)
            {
                MessageBox.Show("No records to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"Payroll_{_employee.EmployeeCode}_{DateTime.Now:yyyyMMdd}.csv",
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Period Start,Period End,Regular Hrs,OT Hrs,Gross Pay,Late (min),Late Ded,Manual Ded,Deductions,Net Pay,Status");

                foreach (var p in items)
                {
                    sb.AppendLine(string.Join(",",
                        p.PayPeriodStart.ToString("yyyy-MM-dd"),
                        p.PayPeriodEnd.ToString("yyyy-MM-dd"),
                        p.RegularHours.ToString("N2"),
                        p.OvertimeHours.ToString("N2"),
                        p.GrossPay.ToString("N2"),
                        p.TotalTardinessMinutes.ToString(),
                        p.TardinessDeduction.ToString("N2"),
                        p.ManualDeduction.ToString("N2"),
                        p.Deductions.ToString("N2"),
                        p.NetPay.ToString("N2"),
                        p.Status));
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
                MessageBox.Show($"Failed to export.\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadPayrolls();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
