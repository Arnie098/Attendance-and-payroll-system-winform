using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        private enum AttendanceSlot { TimeInAM, TimeOutAM, TimeInPM, TimeOutPM }
        private AttendanceSlot? _targetSlot;

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
            HeaderDateText.Text = DateTime.Now.ToString("MMMM dd, yyyy");
            AttendanceCrudTab.Visibility = _allowCrud ? Visibility.Visible : Visibility.Collapsed;
            DeleteTodayButton.Visibility = _allowCrud ? Visibility.Visible : Visibility.Collapsed;
            if (_allowCrud)
            {
                CrudStatusComboBox.SelectedIndex = 0;
                ResetCrudForm();
                LoadAttendanceRecords();
            }

            LoadTodayAttendance();
        }

        // ── Biometric ────────────────────────────────────────────────────────────

        private async void BiometricSimulation_Click(object sender, RoutedEventArgs e)
        {
            var todayAttendance = _attendanceRepository.GetTodayAttendance(_employee.EmployeeId);
            if (todayAttendance != null && LeavePolicies.IsLeaveAttendanceStatus(todayAttendance.Status))
            {
                _viewModel.IsScanButtonEnabled = false;
                _viewModel.ScanStateText = "Attendance locked";
                _viewModel.StatusText = $"Approved leave is already recorded for today as {todayAttendance.Status}.";
                MessageBox.Show(
                    "Approved leave is already recorded for today. Biometric attendance is disabled while leave is active.",
                    "Leave Scheduled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

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

            _viewModel.ScanStateText = "Fingerprint verified";
            RecordSelectedSlot(biometricVerified: true);
        }

        // ── Barcode fallback ─────────────────────────────────────────────────────

        private void BarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                e.Handled = true;
                ProcessBarcodeInput();
            }
        }

        private void BarcodeScan_Click(object sender, RoutedEventArgs e)
        {
            ProcessBarcodeInput();
        }

        private void ProcessBarcodeInput()
        {
            var code = BarcodeTextBox.Text?.Trim() ?? string.Empty;
            BarcodeTextBox.Text = string.Empty;
            BarcodeTextBox.Focus();

            if (string.IsNullOrWhiteSpace(code))
            {
                ShowBarcodeStatus("Please scan or enter the employee barcode.", isError: true);
                return;
            }

            if (!string.Equals(code, _employee.EmployeeCode, StringComparison.OrdinalIgnoreCase))
            {
                ShowBarcodeStatus($"Code \"{code}\" does not match this employee. Expected: {_employee.EmployeeCode}", isError: true);
                return;
            }

            var todayAttendance = _attendanceRepository.GetTodayAttendance(_employee.EmployeeId);
            if (todayAttendance != null && LeavePolicies.IsLeaveAttendanceStatus(todayAttendance.Status))
            {
                ShowBarcodeStatus($"Attendance locked — leave is recorded for today ({todayAttendance.Status}).", isError: true);
                return;
            }

            _viewModel.ScanStateText = "Barcode accepted";
            _viewModel.LastScanText = $"Last Scan: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (barcode)";
            ShowBarcodeStatus("Barcode accepted. Recording slot…", isError: false);

            RecordSelectedSlot(biometricVerified: false);
        }

        private void ShowBarcodeStatus(string message, bool isError)
        {
            BarcodeStatusText.Text = message;
            BarcodeStatusText.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))
                : new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E));
            BarcodeStatusText.Visibility = Visibility.Visible;
        }

        private void ShowQrCode_Click(object sender, RoutedEventArgs e)
        {
            var modal = new QrCodeModal(_employee) { Owner = this };
            modal.ShowDialog();
        }

        // ── Tile slot selection ──────────────────────────────────────────────────

        private void TileTimeInAM_Click(object sender, RoutedEventArgs e)  => SelectSlot(AttendanceSlot.TimeInAM);
        private void TileTimeOutAM_Click(object sender, RoutedEventArgs e) => SelectSlot(AttendanceSlot.TimeOutAM);
        private void TileTimeInPM_Click(object sender, RoutedEventArgs e)  => SelectSlot(AttendanceSlot.TimeInPM);
        private void TileTimeOutPM_Click(object sender, RoutedEventArgs e) => SelectSlot(AttendanceSlot.TimeOutPM);

        private void SelectSlot(AttendanceSlot slot)
        {
            _targetSlot = slot;

            TileTimeInAM.Tag  = null;
            TileTimeOutAM.Tag = null;
            TileTimeInPM.Tag  = null;
            TileTimeOutPM.Tag = null;

            var tileMap = new System.Collections.Generic.Dictionary<AttendanceSlot, Button>
            {
                { AttendanceSlot.TimeInAM,  TileTimeInAM  },
                { AttendanceSlot.TimeOutAM, TileTimeOutAM },
                { AttendanceSlot.TimeInPM,  TileTimeInPM  },
                { AttendanceSlot.TimeOutPM, TileTimeOutPM },
            };
            tileMap[slot].Tag = "Selected";

            var label = slot switch
            {
                AttendanceSlot.TimeInAM  => "AM Time In",
                AttendanceSlot.TimeOutAM => "AM Time Out",
                AttendanceSlot.TimeInPM  => "PM Time In",
                AttendanceSlot.TimeOutPM => "PM Time Out",
                _                        => slot.ToString()
            };
            _viewModel.ScanStateText  = $"Ready — scan to record {label}";
            _viewModel.StatusText     = $"Slot selected: {label}. Scan fingerprint or enter barcode to record.";
            _viewModel.NextActionText = $"Recording: {label}";
        }

        // ── Slot-targeted recording ──────────────────────────────────────────────

        private void RecordSelectedSlot(bool biometricVerified)
        {
            if (_targetSlot == null)
            {
                _viewModel.StatusText    = "Click a session tile above to select a slot first.";
                _viewModel.ScanStateText = "No slot selected";
                return;
            }

            var slotColumn = _targetSlot.Value.ToString();

            var todayAttendance = _attendanceRepository.GetTodayAttendance(_employee.EmployeeId);
            if (todayAttendance != null && LeavePolicies.IsLeaveAttendanceStatus(todayAttendance.Status))
            {
                _viewModel.StatusText = $"Attendance locked — leave is recorded ({todayAttendance.Status}).";
                return;
            }

            if (todayAttendance != null)
            {
                var existing = GetSlotValue(todayAttendance, _targetSlot.Value);
                if (existing.HasValue)
                {
                    var confirm = MessageBox.Show(
                        $"This slot already has {existing.Value:hh:mm tt}.\nOverwrite with current time?",
                        "Confirm Overwrite",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes)
                        return;
                }
            }

            try
            {
                _attendanceRepository.RecordSpecificSlot(_employee.EmployeeId, slotColumn, biometricVerified);
                var method = biometricVerified ? "biometric" : "barcode";
                _viewModel.StatusText = $"Slot recorded via {method}.";
                if (biometricVerified)
                    MessageBox.Show("Slot recorded successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    ShowBarcodeStatus("Slot recorded via barcode.", isError: false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to record slot.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _targetSlot = null;
            TileTimeInAM.Tag = TileTimeOutAM.Tag = TileTimeInPM.Tag = TileTimeOutPM.Tag = null;
            _viewModel.ScanStateText  = "Ready for fingerprint verification";
            _viewModel.NextActionText = "Select a session tile above";

            LoadTodayAttendance();
            if (_allowCrud)
                LoadAttendanceRecords();
        }

        private static DateTime? GetSlotValue(Attendance a, AttendanceSlot slot) => slot switch
        {
            AttendanceSlot.TimeInAM  => a.TimeInAM,
            AttendanceSlot.TimeOutAM => a.TimeOutAM,
            AttendanceSlot.TimeInPM  => a.TimeInPM,
            AttendanceSlot.TimeOutPM => a.TimeOutPM,
            _                        => null
        };

        private void LoadTodayAttendance()
        {
            var todayAttendance = _attendanceRepository.GetTodayAttendance(_employee.EmployeeId);

            // Update the Delete Today button — only relevant when a record exists
            if (_allowCrud)
                DeleteTodayButton.IsEnabled = todayAttendance != null
                    && !LeavePolicies.IsLeaveAttendanceStatus(todayAttendance.Status);

            if (todayAttendance == null)
            {
                _viewModel.TimeInAMText = "-";
                _viewModel.TimeOutAMText = "-";
                _viewModel.TimeInPMText = "-";
                _viewModel.TimeOutPMText = "-";
                _viewModel.NextActionText = "Select a session tile above";
                _viewModel.IsScanButtonEnabled = true;
                if (string.IsNullOrWhiteSpace(_viewModel.StatusText))
                    _viewModel.StatusText = "Click a session tile to start recording attendance.";
                return;
            }

            if (LeavePolicies.IsLeaveAttendanceStatus(todayAttendance.Status))
            {
                _viewModel.TimeInAMText = "-";
                _viewModel.TimeOutAMText = "-";
                _viewModel.TimeInPMText = "-";
                _viewModel.TimeOutPMText = "-";
                _viewModel.NextActionText = "No pending action";
                _viewModel.StatusText = $"Approved leave recorded for today as {todayAttendance.Status}.";
                _viewModel.ScanStateText = "Attendance locked";
                _viewModel.IsScanButtonEnabled = false;
                return;
            }

            _viewModel.TimeInAMText = todayAttendance.TimeInAM?.ToString("hh:mm tt") ?? "-";
            _viewModel.TimeOutAMText = todayAttendance.TimeOutAM?.ToString("hh:mm tt") ?? "-";
            _viewModel.TimeInPMText = todayAttendance.TimeInPM?.ToString("hh:mm tt") ?? "-";
            _viewModel.TimeOutPMText = todayAttendance.TimeOutPM?.ToString("hh:mm tt") ?? "-";

            _viewModel.NextActionText = "Select a session tile above";
            if (_targetSlot == null)
                _viewModel.StatusText = "Click a session tile to choose which slot to record.";
            _viewModel.ScanStateText = "Ready for fingerprint verification";
            _viewModel.IsScanButtonEnabled = true;
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
            CrudTimeInAMTextBox.Text = selected.TimeInAM?.ToString("HH:mm") ?? string.Empty;
            CrudTimeOutAMTextBox.Text = selected.TimeOutAM?.ToString("HH:mm") ?? string.Empty;
            CrudTimeInPMTextBox.Text = selected.TimeInPM?.ToString("HH:mm") ?? string.Empty;
            CrudTimeOutPMTextBox.Text = selected.TimeOutPM?.ToString("HH:mm") ?? string.Empty;
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

            var baseDate = CrudDatePicker.SelectedDate.Value;

            if (!TryParseTime(baseDate, CrudTimeInAMTextBox.Text, out var timeInAM))
            {
                MessageBox.Show("Time In AM format is invalid. Use HH:mm.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryParseTime(baseDate, CrudTimeOutAMTextBox.Text, out var timeOutAM))
            {
                MessageBox.Show("Time Out AM format is invalid. Use HH:mm.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryParseTime(baseDate, CrudTimeInPMTextBox.Text, out var timeInPM))
            {
                MessageBox.Show("Time In PM format is invalid. Use HH:mm.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryParseTime(baseDate, CrudTimeOutPMTextBox.Text, out var timeOutPM))
            {
                MessageBox.Show("Time Out PM format is invalid. Use HH:mm.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (timeInAM.HasValue && timeOutAM.HasValue && timeOutAM.Value < timeInAM.Value)
            {
                MessageBox.Show("Morning Time Out cannot be earlier than Morning Time In.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (timeInPM.HasValue && timeOutPM.HasValue && timeOutPM.Value < timeInPM.Value)
            {
                MessageBox.Show("Afternoon Time Out cannot be earlier than Afternoon Time In.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var status = GetSelectedCrudStatus();
            if (string.IsNullOrWhiteSpace(status))
            {
                MessageBox.Show("Status is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (LeavePolicies.IsLeaveAttendanceStatus(status) && (timeInAM.HasValue || timeOutAM.HasValue || timeInPM.HasValue || timeOutPM.HasValue))
            {
                MessageBox.Show("Leave records cannot store Time In or Time Out values.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            attendance.AttendanceDate = baseDate.Date;
            attendance.TimeInAM = timeInAM;
            attendance.TimeOutAM = timeOutAM;
            attendance.TimeInPM = timeInPM;
            attendance.TimeOutPM = timeOutPM;
            attendance.Status = status;
            attendance.IsBiometricVerified = LeavePolicies.IsLeaveAttendanceStatus(status)
                ? false
                : CrudBiometricVerifiedCheckBox.IsChecked == true;
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
            CrudTimeInAMTextBox.Text = string.Empty;
            CrudTimeOutAMTextBox.Text = string.Empty;
            CrudTimeInPMTextBox.Text = string.Empty;
            CrudTimeOutPMTextBox.Text = string.Empty;
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

        private void DeleteTodayAttendance_Click(object sender, RoutedEventArgs e)
        {
            var todayAttendance = _attendanceRepository.GetTodayAttendance(_employee.EmployeeId);
            if (todayAttendance == null)
            {
                MessageBox.Show("No attendance record found for today.", "Delete Today", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete today's attendance record for {_employee.FullName}?\n\nThis will remove all recorded times for {DateTime.Today:yyyy-MM-dd} and allow re-simulation.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                _attendanceRepository.DeleteAttendance(todayAttendance.AttendanceId);
                _viewModel.ScanStateText = "Ready for fingerprint verification";
                _viewModel.StatusText = "Today's record deleted. Ready to record attendance.";
                LoadTodayAttendance();
                if (_allowCrud)
                    LoadAttendanceRecords();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete attendance record.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        private string _timeInAMText = string.Empty;
        private string _timeOutAMText = string.Empty;
        private string _timeInPMText = string.Empty;
        private string _timeOutPMText = string.Empty;
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

        public string TimeInAMText
        {
            get => _timeInAMText;
            set => SetProperty(ref _timeInAMText, value);
        }

        public string TimeOutAMText
        {
            get => _timeOutAMText;
            set => SetProperty(ref _timeOutAMText, value);
        }

        public string TimeInPMText
        {
            get => _timeInPMText;
            set => SetProperty(ref _timeInPMText, value);
        }

        public string TimeOutPMText
        {
            get => _timeOutPMText;
            set => SetProperty(ref _timeOutPMText, value);
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
