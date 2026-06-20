using System;
using System.Windows;
using System.Windows.Controls;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem
{
    public partial class LeaveRequestModal : Window
    {
        private readonly Employee _employee;

        public LeaveRequest? ResultLeaveRequest { get; private set; }

        public LeaveRequestModal(Employee employee)
        {
            InitializeComponent();
            _employee = employee;

            EmployeeNameTextBlock.Text = employee.FullName;
            EmployeeCodeTextBlock.Text = $"Code: {employee.EmployeeCode}";

            LeaveTypeComboBox.ItemsSource = LeavePolicies.DefaultLeaveTypes;
            LeaveTypeComboBox.SelectedIndex = 0;
            StartDatePicker.SelectedDate = DateTime.Today;
            EndDatePicker.SelectedDate = DateTime.Today;

            RefreshLeaveSummary();
        }

        private void LeaveTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshLeaveSummary();
        }

        private void LeaveDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshLeaveSummary();
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            var leaveType = LeaveTypeComboBox.SelectedItem?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(leaveType))
            {
                MessageBox.Show("Leave type is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!LeavePolicies.TryGetPaidLeaveType(leaveType, out var isPaidLeave))
            {
                MessageBox.Show("Please select a valid leave type.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!StartDatePicker.SelectedDate.HasValue || !EndDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Start date and end date are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startDate = StartDatePicker.SelectedDate.Value.Date;
            var endDate = EndDatePicker.SelectedDate.Value.Date;
            if (endDate < startDate)
            {
                MessageBox.Show("End date cannot be earlier than start date.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var chargeableDays = LeavePolicies.GetChargeableDayCount(startDate, endDate);
            if (chargeableDays == 0)
            {
                MessageBox.Show("The selected date range must include at least one weekday.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var reason = ReasonTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("Reason is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultLeaveRequest = new LeaveRequest
            {
                EmployeeId = _employee.EmployeeId,
                EmployeeCode = _employee.EmployeeCode,
                EmployeeName = _employee.FullName,
                LeaveType = leaveType,
                IsPaid = isPaidLeave,
                StartDate = startDate,
                EndDate = endDate,
                Reason = reason,
                Status = LeavePolicies.StatusPending
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RefreshLeaveSummary()
        {
            var leaveType = LeaveTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
            var hasKnownLeaveType = LeavePolicies.TryGetPaidLeaveType(leaveType, out var isPaidLeave);

            PaymentTypeTextBox.Text = !hasKnownLeaveType
                ? "-"
                : isPaidLeave ? "Paid leave" : "Unpaid leave";
            AttendanceStatusTextBlock.Text = !hasKnownLeaveType
                ? "-"
                : LeavePolicies.GetAttendanceStatus(isPaidLeave);

            if (!StartDatePicker.SelectedDate.HasValue || !EndDatePicker.SelectedDate.HasValue)
            {
                ChargeableDaysTextBlock.Text = "-";
                return;
            }

            var startDate = StartDatePicker.SelectedDate.Value.Date;
            var endDate = EndDatePicker.SelectedDate.Value.Date;
            ChargeableDaysTextBlock.Text = endDate < startDate
                ? "0"
                : LeavePolicies.GetChargeableDayCount(startDate, endDate).ToString();
        }
    }
}
