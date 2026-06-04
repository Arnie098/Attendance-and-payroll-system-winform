using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem
{
    public partial class PayrollEditWindow : Window
    {
        private readonly Payroll _originalPayroll;

        public Payroll? ResultPayroll { get; private set; }
        public bool DeleteRequested { get; private set; }

        public PayrollEditWindow(Employee employee, Payroll payroll)
        {
            InitializeComponent();
            _originalPayroll = payroll;
            EmployeeTextBlock.Text = $"{employee.EmployeeCode} - {employee.FullName}";
            LoadPayroll(payroll);
        }

        private void LoadPayroll(Payroll payroll)
        {
            ManualPeriodStartPicker.SelectedDate = payroll.PayPeriodStart;
            ManualPeriodEndPicker.SelectedDate = payroll.PayPeriodEnd;
            ManualRegularHoursTextBox.Text = payroll.RegularHours.ToString("N2", CultureInfo.InvariantCulture);
            ManualOvertimeHoursTextBox.Text = payroll.OvertimeHours.ToString("N2", CultureInfo.InvariantCulture);
            ManualGrossPayTextBox.Text = payroll.GrossPay.ToString("N2", CultureInfo.InvariantCulture);
            ManualDeductionsTextBox.Text = payroll.Deductions.ToString("N2", CultureInfo.InvariantCulture);
            ManualNetPayTextBox.Text = payroll.NetPay.ToString("N2", CultureInfo.InvariantCulture);
            ManualManualDeductionTextBox.Text = payroll.ManualDeduction.ToString("N2", CultureInfo.InvariantCulture);
            ManualManualDeductionNoteTextBox.Text = payroll.ManualDeductionNote;
            SelectStatus(payroll.Status);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildPayroll(out var payroll))
            {
                return;
            }

            ResultPayroll = payroll;
            DialogResult = true;
            Close();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Delete selected payroll record?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            DeleteRequested = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool TryBuildPayroll(out Payroll payroll)
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

            if (!TryParseNonNegativeDecimal(ManualRegularHoursTextBox.Text, "Regular hours", out var regularHours) ||
                !TryParseNonNegativeDecimal(ManualOvertimeHoursTextBox.Text, "Overtime hours", out var overtimeHours) ||
                !TryParseNonNegativeDecimal(ManualGrossPayTextBox.Text, "Gross pay", out var grossPay) ||
                !TryParseNonNegativeDecimal(ManualDeductionsTextBox.Text, "Deductions", out var deductions) ||
                !TryParseNonNegativeDecimal(ManualNetPayTextBox.Text, "Net pay", out var netPay))
            {
                return false;
            }

            decimal manualDeduction = 0m;
            if (!string.IsNullOrWhiteSpace(ManualManualDeductionTextBox.Text) &&
                !decimal.TryParse(ManualManualDeductionTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out manualDeduction))
            {
                MessageBox.Show("Manual deduction must be a valid number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (manualDeduction < 0)
            {
                MessageBox.Show("Manual deduction cannot be negative.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            payroll.PayrollId = _originalPayroll.PayrollId;
            payroll.EmployeeId = _originalPayroll.EmployeeId;
            payroll.EmployeeName = _originalPayroll.EmployeeName;
            payroll.EmployeeCode = _originalPayroll.EmployeeCode;
            payroll.PayPeriodStart = ManualPeriodStartPicker.SelectedDate.Value.Date;
            payroll.PayPeriodEnd = ManualPeriodEndPicker.SelectedDate.Value.Date;
            payroll.RegularHours = regularHours;
            payroll.OvertimeHours = overtimeHours;
            payroll.GrossPay = grossPay;
            payroll.Deductions = deductions;
            payroll.NetPay = netPay;
            payroll.ManualDeduction = manualDeduction;
            payroll.ManualDeductionNote = ManualManualDeductionNoteTextBox.Text.Trim();
            payroll.Status = GetSelectedStatus();
            return true;
        }

        private static bool TryParseNonNegativeDecimal(string rawValue, string label, out decimal value)
        {
            if (!decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out value) || value < 0)
            {
                MessageBox.Show($"{label} must be a valid non-negative number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
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
    }
}
