using System;
using System.Collections.ObjectModel;
using System.Globalization;
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

        public PayrollModal(Employee employee)
        {
            InitializeComponent();
            _employee = employee;
            _viewModel.EmployeeDisplay = $"{employee.EmployeeCode} - {employee.FullName}";
            _viewModel.PeriodEnd = DateTime.Today;
            _viewModel.PeriodStart = DateTime.Today.AddDays(-14);
            DataContext = _viewModel;

            ManualStatusComboBox.SelectedIndex = 0;
            ResetForm();
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

            try
            {
                var payroll = _payrollCalculator.CalculatePayroll(_employee, _viewModel.PeriodStart.Value, _viewModel.PeriodEnd.Value);
                _payrollRepository.AddPayroll(payroll);
                LoadPayrolls();

                MessageBox.Show(
                    $"Payroll calculated.\nGross: PHP {payroll.GrossPay:N2}\nNet: PHP {payroll.NetPay:N2}",
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
            if (PayrollDataGrid.SelectedItem is not Payroll selected)
            {
                return;
            }

            _selectedPayroll = selected;
            ManualPeriodStartPicker.SelectedDate = selected.PayPeriodStart;
            ManualPeriodEndPicker.SelectedDate = selected.PayPeriodEnd;
            ManualRegularHoursTextBox.Text = selected.RegularHours.ToString("N2", CultureInfo.InvariantCulture);
            ManualOvertimeHoursTextBox.Text = selected.OvertimeHours.ToString("N2", CultureInfo.InvariantCulture);
            ManualGrossPayTextBox.Text = selected.GrossPay.ToString("N2", CultureInfo.InvariantCulture);
            ManualDeductionsTextBox.Text = selected.Deductions.ToString("N2", CultureInfo.InvariantCulture);
            ManualNetPayTextBox.Text = selected.NetPay.ToString("N2", CultureInfo.InvariantCulture);
            SelectStatus(selected.Status);
        }

        private void AddPayroll_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildPayrollFromForm(out var payroll))
            {
                return;
            }

            try
            {
                payroll.EmployeeId = _employee.EmployeeId;
                _payrollRepository.AddPayroll(payroll);
                LoadPayrolls();
                MessageBox.Show("Payroll record added successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add payroll record.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePayroll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPayroll == null)
            {
                MessageBox.Show("Select a payroll record to update.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryBuildPayrollFromForm(out var payroll))
            {
                return;
            }

            try
            {
                payroll.PayrollId = _selectedPayroll.PayrollId;
                payroll.EmployeeId = _employee.EmployeeId;
                _payrollRepository.UpdatePayroll(payroll);
                LoadPayrolls();
                MessageBox.Show("Payroll record updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update payroll record.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeletePayroll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPayroll == null)
            {
                MessageBox.Show("Select a payroll record to delete.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private void ClearForm_Click(object sender, RoutedEventArgs e)
        {
            _selectedPayroll = null;
            PayrollDataGrid.SelectedItem = null;
            ResetForm();
        }

        private void LoadPayrolls()
        {
            _viewModel.Payrolls.Clear();
            var payrolls = _payrollRepository.GetPayrollByEmployee(_employee.EmployeeId);
            foreach (var payroll in payrolls)
            {
                _viewModel.Payrolls.Add(payroll);
            }

            _selectedPayroll = null;
            PayrollDataGrid.SelectedItem = null;
            ResetForm();
        }

        private bool TryBuildPayrollFromForm(out Payroll payroll)
        {
            payroll = new Payroll();

            if (!ManualPeriodStartPicker.SelectedDate.HasValue || !ManualPeriodEndPicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Payroll period dates are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (ManualPeriodStartPicker.SelectedDate.Value > ManualPeriodEndPicker.SelectedDate.Value)
            {
                MessageBox.Show("Period start must be on or before period end.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(ManualRegularHoursTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var regularHours) || regularHours < 0)
            {
                MessageBox.Show("Regular hours must be a valid non-negative number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(ManualOvertimeHoursTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var overtimeHours) || overtimeHours < 0)
            {
                MessageBox.Show("Overtime hours must be a valid non-negative number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(ManualGrossPayTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var grossPay) || grossPay < 0)
            {
                MessageBox.Show("Gross pay must be a valid non-negative number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(ManualDeductionsTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var deductions) || deductions < 0)
            {
                MessageBox.Show("Deductions must be a valid non-negative number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(ManualNetPayTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var netPay) || netPay < 0)
            {
                MessageBox.Show("Net pay must be a valid non-negative number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            payroll.PayPeriodStart = ManualPeriodStartPicker.SelectedDate.Value.Date;
            payroll.PayPeriodEnd = ManualPeriodEndPicker.SelectedDate.Value.Date;
            payroll.RegularHours = regularHours;
            payroll.OvertimeHours = overtimeHours;
            payroll.GrossPay = grossPay;
            payroll.Deductions = deductions;
            payroll.NetPay = netPay;
            payroll.Status = GetSelectedStatus();
            payroll.EmployeeName = _employee.FullName;
            payroll.EmployeeCode = _employee.EmployeeCode;
            return true;
        }

        private void ResetForm()
        {
            ManualPeriodStartPicker.SelectedDate = DateTime.Today.AddDays(-14);
            ManualPeriodEndPicker.SelectedDate = DateTime.Today;
            ManualRegularHoursTextBox.Text = "0.00";
            ManualOvertimeHoursTextBox.Text = "0.00";
            ManualGrossPayTextBox.Text = "0.00";
            ManualDeductionsTextBox.Text = "0.00";
            ManualNetPayTextBox.Text = "0.00";
            ManualStatusComboBox.SelectedIndex = 0;
        }

        private void SelectStatus(string status)
        {
            foreach (var item in ManualStatusComboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(comboItem.Content?.ToString(), status, StringComparison.OrdinalIgnoreCase))
                {
                    ManualStatusComboBox.SelectedItem = comboItem;
                    return;
                }
            }

            ManualStatusComboBox.SelectedIndex = 0;
        }

        private string GetSelectedStatus()
        {
            return ManualStatusComboBox.SelectedItem is ComboBoxItem selected
                ? selected.Content?.ToString() ?? "Pending"
                : "Pending";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
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
