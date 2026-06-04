using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        public PayrollModal(Employee employee)
        {
            InitializeComponent();
            _employee = employee;
            _viewModel.EmployeeDisplay = $"{employee.EmployeeCode} - {employee.FullName}";
            _viewModel.PeriodEnd = DateTime.Today;
            _viewModel.PeriodStart = DateTime.Today.AddDays(-14);
            DataContext = _viewModel;
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
                var payroll = _payrollCalculator.CalculatePayroll(_employee, _viewModel.PeriodStart.Value, _viewModel.PeriodEnd.Value, manualDeduction, manualDeductionNote);
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

            var editWindow = new PayrollEditWindow(_employee, _selectedPayroll)
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
                    _payrollRepository.DeletePayroll(_selectedPayroll.PayrollId);
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
