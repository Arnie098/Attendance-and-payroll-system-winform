using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using Microsoft.Win32;

namespace AttendancePayrollSystem
{
    public partial class LeaveRequestModal : Window
    {
        private readonly Employee _employee;
        private readonly ObservableCollection<DocumentDraftItem> _drafts = new();

        public LeaveRequest? ResultLeaveRequest { get; private set; }
        public IReadOnlyList<DocumentDraftItem> ResultDocuments => _drafts;

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

            DocumentsListBox.ItemsSource = _drafts;
            RefreshLeaveSummary();
        }

        private void LeaveTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => RefreshLeaveSummary();

        private void LeaveDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
            => RefreshLeaveSummary();

        private void AddDocuments_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Attach Supporting Documents",
                Filter = "Documents|*.pdf;*.jpg;*.jpeg;*.png;*.doc;*.docx|All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            const long MaxBytes = 10L * 1024 * 1024;
            var skipped = new List<string>();

            foreach (var path in dialog.FileNames)
            {
                var info = new FileInfo(path);
                if (info.Length > MaxBytes)
                {
                    skipped.Add(info.Name);
                    continue;
                }
                _drafts.Add(new DocumentDraftItem(info.Name, File.ReadAllBytes(path)));
            }

            if (skipped.Count > 0)
                MessageBox.Show(this,
                    $"The following file(s) exceed the 10 MB limit and were skipped:\n• {string.Join("\n• ", skipped)}",
                    "Files Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void RemoveDocument_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: DocumentDraftItem item })
                _drafts.Remove(item);
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

            if (LeavePolicies.GetChargeableDayCount(startDate, endDate) == 0)
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

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void RefreshLeaveSummary()
        {
            var leaveType = LeaveTypeComboBox.SelectedItem?.ToString() ?? string.Empty;
            var hasKnownLeaveType = LeavePolicies.TryGetPaidLeaveType(leaveType, out var isPaidLeave);

            PaymentTypeTextBox.Text = !hasKnownLeaveType ? "-" : isPaidLeave ? "Paid leave" : "Unpaid leave";
            AttendanceStatusTextBlock.Text = !hasKnownLeaveType ? "-" : LeavePolicies.GetAttendanceStatus(isPaidLeave);

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

    public sealed class DocumentDraftItem
    {
        public string Name { get; }
        public byte[] Data { get; }
        public string SizeLabel { get; }

        public DocumentDraftItem(string name, byte[] data)
        {
            Name = name;
            Data = data;
            var kb = data.Length / 1024.0;
            SizeLabel = kb >= 1024 ? $"{kb / 1024.0:N1} MB" : $"{kb:N1} KB";
        }
    }
}
