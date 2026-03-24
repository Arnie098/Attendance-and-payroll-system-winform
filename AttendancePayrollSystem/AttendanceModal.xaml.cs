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
    public partial class AttendanceModal : Window
    {
        private readonly Employee _employee;
        private readonly bool _allowCrud;
        private readonly AttendanceRepository _attendanceRepository = new();
        private readonly BiometricSimulator _biometricSimulator = new();
        private readonly AttendanceModalViewModel _viewModel;
        private Attendance? _selectedAttendance;

        public AttendanceModal(Employee employee, bool allowCrud = true)
        {
            InitializeComponent();
            _employee = employee;
            _allowCrud = allowCrud;
            _viewModel = new AttendanceModalViewModel
            {
                HeaderText = "Biometric Attendance Terminal",
                EmployeeNameText = $"{employee.EmployeeCode} - {employee.FullName}",
                ScanStateText = "Ready for fingerprint verification",
                LastScanText = "Last Scan: -",
                IsScanButtonEnabled = true
            };

            DataContext = _viewModel;
            AttendanceCrudTab.Visibility = _allowCrud ? Visibility.Visible : Visibility.Collapsed;
            if (_allowCrud)
            {
                CrudStatusComboBox.SelectedIndex = 0;
                ResetCrudForm();
                LoadAttendanceRecords();
            }

            LoadTodayAttendance();
        }

        private async void BiometricSimulation_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.IsScanning = true;
            _viewModel.IsScanButtonEnabled = false;
            _viewModel.ScanStateText = "Scanning fingerprint...";
            _viewModel.StatusText = "Placing finger on sensor and validating biometric template.";

            var result = await _biometricSimulator.SimulateFingerprint(_employee.EmployeeId);
            _viewModel.LastScanText = $"Last Scan: {result.Timestamp:yyyy-MM-dd HH:mm:ss}";
            _viewModel.IsScanning = false;
            _viewModel.IsScanButtonEnabled = true;

            if (!result.Success)
            {
                _viewModel.ScanStateText = "Verification failed";
                _viewModel.StatusText = result.Message;
                MessageBox.Show(result.Message, "Biometric Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _viewModel.ScanStateText = "Verification successful";
            var todayAttendance = _attendanceRepository.GetTodayAttendance(_employee.EmployeeId);

            if (todayAttendance == null)
            {
                _attendanceRepository.RecordTimeIn(_employee.EmployeeId, true);
                _viewModel.StatusText = "Time In recorded.";
                MessageBox.Show("Time In recorded successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (!todayAttendance.TimeOut.HasValue)
            {
                _attendanceRepository.RecordTimeOut(todayAttendance.AttendanceId);
                _viewModel.StatusText = "Time Out recorded.";
                MessageBox.Show("Time Out recorded successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _viewModel.StatusText = "Attendance is already complete for today.";
                MessageBox.Show("Attendance already completed for today.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            LoadTodayAttendance();
            if (_allowCrud)
            {
                LoadAttendanceRecords();
            }
        }

        private void LoadTodayAttendance()
        {
            var todayAttendance = _attendanceRepository.GetTodayAttendance(_employee.EmployeeId);
            if (todayAttendance == null)
            {
                _viewModel.TimeInText = "-";
                _viewModel.TimeOutText = "-";
                _viewModel.NextActionText = "Time In";
                if (string.IsNullOrWhiteSpace(_viewModel.StatusText))
                {
                    _viewModel.StatusText = "No attendance record yet for today.";
                }

                return;
            }

            _viewModel.TimeInText = todayAttendance.TimeIn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            _viewModel.TimeOutText = todayAttendance.TimeOut?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            _viewModel.NextActionText = todayAttendance.TimeOut.HasValue ? "No pending action" : "Time Out";
            _viewModel.StatusText = todayAttendance.TimeOut.HasValue
                ? "Attendance completed."
                : "Employee is currently clocked in.";
        }

        private void LoadAttendanceRecords()
        {
            _viewModel.AttendanceRecords.Clear();
            var records = _attendanceRepository.GetAttendanceByEmployee(_employee.EmployeeId);
            foreach (var attendance in records)
            {
                _viewModel.AttendanceRecords.Add(attendance);
            }

            ClearSelectedAttendance();
        }

        private void AttendanceCrudDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AttendanceCrudDataGrid.SelectedItem is not Attendance selected)
            {
                ClearSelectedAttendance();
                return;
            }

            _selectedAttendance = selected;
            _viewModel.HasSelectedAttendance = true;
            CrudDatePicker.SelectedDate = selected.AttendanceDate.Date;
            CrudTimeInTextBox.Text = selected.TimeIn?.ToString("HH:mm") ?? string.Empty;
            CrudTimeOutTextBox.Text = selected.TimeOut?.ToString("HH:mm") ?? string.Empty;
            SelectCrudStatus(selected.Status);
            CrudBiometricVerifiedCheckBox.IsChecked = selected.IsBiometricVerified;
        }

        private void UpdateAttendanceCrud_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAttendance == null)
            {
                MessageBox.Show("Select an attendance record to update.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryBuildAttendanceFromForm(out var attendance))
            {
                return;
            }

            try
            {
                attendance.AttendanceId = _selectedAttendance.AttendanceId;
                attendance.EmployeeId = _employee.EmployeeId;
                _attendanceRepository.UpdateAttendance(attendance);
                LoadAttendanceRecords();
                LoadTodayAttendance();
                MessageBox.Show("Attendance record updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update attendance record.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteAttendanceCrud_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAttendance == null)
            {
                MessageBox.Show("Select an attendance record to delete.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Delete selected attendance record?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _attendanceRepository.DeleteAttendance(_selectedAttendance.AttendanceId);
                LoadAttendanceRecords();
                LoadTodayAttendance();
                MessageBox.Show("Attendance record deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete attendance record.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshAttendanceCrud_Click(object sender, RoutedEventArgs e)
        {
            LoadAttendanceRecords();
            LoadTodayAttendance();
        }

        private void ClearAttendanceCrud_Click(object sender, RoutedEventArgs e)
        {
            ClearSelectedAttendance();
        }

        private bool TryBuildAttendanceFromForm(out Attendance attendance)
        {
            attendance = new Attendance();

            if (!CrudDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Attendance date is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryParseTime(CrudDatePicker.SelectedDate.Value, CrudTimeInTextBox.Text, out var timeIn))
            {
                MessageBox.Show("Time In format is invalid. Use HH:mm.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryParseTime(CrudDatePicker.SelectedDate.Value, CrudTimeOutTextBox.Text, out var timeOut))
            {
                MessageBox.Show("Time Out format is invalid. Use HH:mm.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (timeIn.HasValue && timeOut.HasValue && timeOut.Value < timeIn.Value)
            {
                MessageBox.Show("Time Out cannot be earlier than Time In.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var status = GetSelectedCrudStatus();
            if (string.IsNullOrWhiteSpace(status))
            {
                MessageBox.Show("Status is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            attendance.AttendanceDate = CrudDatePicker.SelectedDate.Value.Date;
            attendance.TimeIn = timeIn;
            attendance.TimeOut = timeOut;
            attendance.Status = status;
            attendance.IsBiometricVerified = CrudBiometricVerifiedCheckBox.IsChecked == true;
            return true;
        }

        private static bool TryParseTime(DateTime baseDate, string input, out DateTime? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
            }

            var formats = new[] { "HH:mm", "HH:mm:ss", "h:mm tt", "h:mm:ss tt" };
            if (!DateTime.TryParseExact(input.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return false;
            }

            value = baseDate.Date
                .AddHours(parsed.Hour)
                .AddMinutes(parsed.Minute)
                .AddSeconds(parsed.Second);
            return true;
        }

        private void SelectCrudStatus(string status)
        {
            foreach (var item in CrudStatusComboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(comboItem.Content?.ToString(), status, StringComparison.OrdinalIgnoreCase))
                {
                    CrudStatusComboBox.SelectedItem = comboItem;
                    return;
                }
            }

            CrudStatusComboBox.SelectedIndex = 0;
        }

        private string GetSelectedCrudStatus()
        {
            return CrudStatusComboBox.SelectedItem is ComboBoxItem selected
                ? selected.Content?.ToString() ?? string.Empty
                : string.Empty;
        }

        private void ResetCrudForm()
        {
            CrudDatePicker.SelectedDate = DateTime.Today;
            CrudTimeInTextBox.Text = string.Empty;
            CrudTimeOutTextBox.Text = string.Empty;
            CrudStatusComboBox.SelectedIndex = 0;
            CrudBiometricVerifiedCheckBox.IsChecked = false;
        }

        private void ClearSelectedAttendance()
        {
            _selectedAttendance = null;
            _viewModel.HasSelectedAttendance = false;

            if (AttendanceCrudDataGrid.SelectedItem != null)
            {
                AttendanceCrudDataGrid.SelectedItem = null;
            }

            ResetCrudForm();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class AttendanceModalViewModel : BaseViewModel
    {
        private string _headerText = string.Empty;
        private string _employeeNameText = string.Empty;
        private string _timeInText = string.Empty;
        private string _timeOutText = string.Empty;
        private string _nextActionText = string.Empty;
        private string _statusText = string.Empty;
        private string _scanStateText = string.Empty;
        private string _lastScanText = string.Empty;
        private bool _isScanning;
        private bool _isScanButtonEnabled = true;
        private bool _hasSelectedAttendance;

        public string HeaderText
        {
            get => _headerText;
            set => SetProperty(ref _headerText, value);
        }

        public string EmployeeNameText
        {
            get => _employeeNameText;
            set => SetProperty(ref _employeeNameText, value);
        }

        public string TimeInText
        {
            get => _timeInText;
            set => SetProperty(ref _timeInText, value);
        }

        public string TimeOutText
        {
            get => _timeOutText;
            set => SetProperty(ref _timeOutText, value);
        }

        public string NextActionText
        {
            get => _nextActionText;
            set => SetProperty(ref _nextActionText, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string ScanStateText
        {
            get => _scanStateText;
            set => SetProperty(ref _scanStateText, value);
        }

        public string LastScanText
        {
            get => _lastScanText;
            set => SetProperty(ref _lastScanText, value);
        }

        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }

        public bool IsScanButtonEnabled
        {
            get => _isScanButtonEnabled;
            set => SetProperty(ref _isScanButtonEnabled, value);
        }

        public bool HasSelectedAttendance
        {
            get => _hasSelectedAttendance;
            set => SetProperty(ref _hasSelectedAttendance, value);
        }

        public ObservableCollection<Attendance> AttendanceRecords { get; } = new();
    }
}
