using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem
{
    public partial class PayrollModal : Window
    {
        private readonly Employee _employee;
        private readonly PayrollRepository _payrollRepository = new();
        private readonly PayrollCalculator _payrollCalculator = new();
        private readonly PayrollModalViewModel _viewModel = new();
        private Payroll? _selectedPayroll;
        private System.Collections.Generic.List<Payroll> _allPayrolls = new();

        public PayrollModal(Employee employee, bool isAdminView = true)
        {
            InitializeComponent();
            _employee = employee;
            _viewModel.EmployeeDisplay = $"{employee.EmployeeCode} - {employee.FullName}";
            _viewModel.PeriodEnd = DateTime.Today;
            _viewModel.PeriodStart = DateTime.Today.AddDays(-14);
            DataContext = _viewModel;

            if (!isAdminView)
            {
                CalculateSection.Visibility = Visibility.Collapsed;
                StatutoryRatesSection.Visibility = Visibility.Collapsed;
                PayrollActionsPanel.Visibility = Visibility.Collapsed;
            }

            LoadPayrolls();
        }

        private void CalculatePayroll_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.PeriodStart.HasValue || !_viewModel.PeriodEnd.HasValue)
            {
                MessageBox.Show("Please select payroll period dates.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_viewModel.PeriodStart.Value > _viewModel.PeriodEnd.Value)
            {
                MessageBox.Show("Period start must be on or before period end.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal manualDeduction = 0m;
            if (!string.IsNullOrWhiteSpace(ManualDeductionAmountTextBox.Text) &&
                !decimal.TryParse(ManualDeductionAmountTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out manualDeduction))
            {
                MessageBox.Show("Manual deduction must be a valid number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (manualDeduction < 0)
            {
                MessageBox.Show("Manual deduction cannot be negative.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var manualDeductionNote = ManualDeductionNoteTextBox.Text.Trim();

            try
            {
                var deductionOverrides = TryBuildDeductionOverrides();
                if (deductionOverrides == null)
                {
                    return;
                }

                var payroll = _payrollCalculator.CalculatePayroll(
                    _employee,
                    _viewModel.PeriodStart.Value,
                    _viewModel.PeriodEnd.Value,
                    manualDeduction,
                    manualDeductionNote,
                    deductionOverrides);
                var existingPayroll = _payrollRepository.GetPayrollByEmployeeAndPeriod(
                    _employee.EmployeeId,
                    payroll.PayPeriodStart,
                    payroll.PayPeriodEnd);

                var action = "created";
                if (existingPayroll != null)
                {
                    payroll.PayrollId = existingPayroll.PayrollId;
                    payroll.Status = existingPayroll.Status;
                    _payrollRepository.UpdatePayroll(payroll);
                    action = "updated";
                }
                else
                {
                    _payrollRepository.AddPayroll(payroll);
                }

                LoadPayrolls();

                var manualNote = manualDeduction > 0 ? $"\nManual Deduction: PHP {manualDeduction:N2}" : string.Empty;
                MessageBox.Show(
                    $"Payroll {action}.\nGross: PHP {payroll.GrossPay:N2}{manualNote}\nNet: PHP {payroll.NetPay:N2}",
                    "Payroll Calculated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to calculate payroll.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PayrollDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedPayroll = PayrollDataGrid.SelectedItem as Payroll;
        }

        private void PayrollDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_selectedPayroll == null)
            {
                return;
            }

            EditPayroll(_selectedPayroll);
        }

        private void AddManualPayroll_Click(object sender, RoutedEventArgs e)
        {
            var draftPayroll = BuildManualDraftPayroll();
            var editWindow = new PayrollEditWindow(_employee, draftPayroll)
            {
                Owner = this
            };

            if (editWindow.ShowDialog() != true || editWindow.ResultPayroll == null)
            {
                return;
            }

            try
            {
                _payrollRepository.AddPayroll(editWindow.ResultPayroll);
                LoadPayrolls();
                MessageBox.Show("Payroll record added successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add payroll record.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditSelectedPayroll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPayroll == null)
            {
                MessageBox.Show("Select a payroll record first.", "Edit Payroll", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            EditPayroll(_selectedPayroll);
        }

        private void DeleteSelectedPayroll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPayroll == null)
            {
                MessageBox.Show("Select a payroll record first.", "Delete Payroll", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                "Delete selected payroll record?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _payrollRepository.DeletePayroll(_selectedPayroll.PayrollId);
                LoadPayrolls();
                MessageBox.Show("Payroll record deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete payroll record.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadPayrolls()
        {
            _viewModel.Payrolls.Clear();
            _allPayrolls = _payrollRepository.GetPayrollByEmployee(_employee.EmployeeId);
            foreach (var payroll in _allPayrolls)
            {
                _viewModel.Payrolls.Add(payroll);
            }
            _selectedPayroll = null;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void PrintPayslip_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPayroll == null)
            {
                MessageBox.Show("Select a payroll record first.", "Print Payslip", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                PayslipDocument.Print(_employee, _selectedPayroll);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to print payslip.\n{ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SavePdf_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPayroll == null)
            {
                MessageBox.Show("Select a payroll record first.", "Save as PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                PayslipDocument.SaveAsPdf(_employee, _selectedPayroll);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save payslip as PDF.\n{ex.Message}", "Save as PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private PayrollDeductionOverrides? TryBuildDeductionOverrides()
        {
            if (!TryParseOptionalNonNegativeDecimal(SssDeductionTextBox.Text, "SSS deduction", out var sssDeduction) ||
                !TryParseOptionalNonNegativeDecimal(PhilHealthDeductionTextBox.Text, "PhilHealth deduction", out var philHealthDeduction) ||
                !TryParseOptionalNonNegativeDecimal(PagIbigDeductionTextBox.Text, "Pag-IBIG deduction", out var pagIbigDeduction) ||
                !TryParseOptionalNonNegativeDecimal(WithholdingTaxTextBox.Text, "tax deduction", out var withholdingTax))
            {
                return null;
            }

            return new PayrollDeductionOverrides(
                sssDeduction,
                philHealthDeduction,
                pagIbigDeduction,
                withholdingTax);
        }

        private static bool TryParseOptionalNonNegativeDecimal(string rawValue, string label, out decimal? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return true;
            }

            if (!decimal.TryParse(rawValue.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            {
                MessageBox.Show($"{label} must be a valid non-negative number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            value = parsed;
            return true;
        }

        private void PayrollSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = PayrollSearchBox.Text.Trim();
            _viewModel.Payrolls.Clear();

            var source = string.IsNullOrEmpty(searchText)
                ? _allPayrolls
                : _allPayrolls.FindAll(p =>
                    p.PayPeriodStart.ToString("yyyy-MM-dd").Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.PayPeriodEnd.ToString("yyyy-MM-dd").Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.Status.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.GrossPay.ToString("N2").Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    p.NetPay.ToString("N2").Contains(searchText, StringComparison.OrdinalIgnoreCase));

            foreach (var payroll in source)
            {
                _viewModel.Payrolls.Add(payroll);
            }
        }

        private void EditPayroll(Payroll payroll)
        {
            var editWindow = new PayrollEditWindow(_employee, payroll)
            {
                Owner = this
            };

            if (editWindow.ShowDialog() != true)
            {
                return;
            }

            try
            {
                if (editWindow.DeleteRequested)
                {
                    _payrollRepository.DeletePayroll(payroll.PayrollId);
                    MessageBox.Show("Payroll record deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (editWindow.ResultPayroll != null)
                {
                    _payrollRepository.UpdatePayroll(editWindow.ResultPayroll);
                    MessageBox.Show("Payroll record updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                LoadPayrolls();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save payroll record.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Payroll BuildManualDraftPayroll()
        {
            var periodStart = _viewModel.PeriodStart?.Date ?? DateTime.Today.AddDays(-14);
            var periodEnd = _viewModel.PeriodEnd?.Date ?? DateTime.Today;

            return new Payroll
            {
                EmployeeId = _employee.EmployeeId,
                EmployeeName = _employee.FullName,
                EmployeeCode = _employee.EmployeeCode,
                PayPeriodStart = periodStart,
                PayPeriodEnd = periodEnd,
                Status = "Pending"
            };
        }

    }

    public class PayrollModalViewModel : BaseViewModel
    {
        private string _employeeDisplay = string.Empty;
        private DateTime? _periodStart;
        private DateTime? _periodEnd;
        public ObservableCollection<Payroll> Payrolls { get; } = new();

        public string EmployeeDisplay
        {
            get => _employeeDisplay;
            set => SetProperty(ref _employeeDisplay, value);
        }

        public DateTime? PeriodStart
        {
            get => _periodStart;
            set => SetProperty(ref _periodStart, value);
        }

        public DateTime? PeriodEnd
        {
            get => _periodEnd;
            set => SetProperty(ref _periodEnd, value);
        }

    }
}
