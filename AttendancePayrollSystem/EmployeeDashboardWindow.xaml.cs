using System;
using System.Linq;
using System.Windows;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem
{
    public partial class EmployeeDashboardWindow : Window
    {
        private readonly string _username;
        private readonly AttendanceRepository _attendanceRepository = new();
        private readonly PayrollRepository _payrollRepository = new();
        private readonly EmployeeRepository _employeeRepository = new();
        private readonly LeaveRequestRepository _leaveRequestRepository = new();
        private readonly EmployeeDashboardViewModel _viewModel = new();
        private Employee _employee;
        private LeaveRequest? _selectedLeaveRequest;

        public EmployeeDashboardWindow(Employee employee, string username)
        {
            InitializeComponent();
            _employee = employee;
            _username = username;
            DataContext = _viewModel;

            LoadDashboardData();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadDashboardData();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to refresh dashboard.\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ChangePhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ProfileImageFilePicker.TryPick(this, out var imageBytes))
                {
                    return;
                }

                _employeeRepository.UpdateProfileImage(_employee.EmployeeId, imageBytes);
                _employee.ProfileImage = imageBytes;
                _viewModel.ProfileImage = imageBytes;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to update profile photo.\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RemovePhoto_Click(object sender, RoutedEventArgs e)
        {
            if (_employee.ProfileImage == null || _employee.ProfileImage.Length == 0)
            {
                return;
            }

            try
            {
                _employeeRepository.UpdateProfileImage(_employee.EmployeeId, null);
                _employee.ProfileImage = null;
                _viewModel.ProfileImage = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to remove profile photo.\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClockAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var todayAttendance = _attendanceRepository.GetTodayAttendance(_employee.EmployeeId);
                if (todayAttendance != null && LeavePolicies.IsLeaveAttendanceStatus(todayAttendance.Status))
                {
                    MessageBox.Show(
                        "Approved leave is already recorded for today. Attendance terminal is unavailable while leave is active.",
                        "Leave Scheduled",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var modal = new AttendanceModal(_employee, allowCrud: false)
                {
                    Owner = this
                };
                modal.ShowDialog();
                LoadDashboardData();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open attendance terminal.\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new LoginWindow();
            Application.Current.MainWindow = loginWindow;
            loginWindow.Show();
            Close();
        }

        private void LoadDashboardData()
        {
            LoadEmployeeProfile();
            _viewModel.TodayText = DateTime.Now.ToString("MMMM dd, yyyy");
            LoadTodayAttendanceState();
            LoadAttendanceHistory();
            LoadPayrollHistory();
            LoadLeaveRequests();
        }

        private void LoadEmployeeProfile()
        {
            var latestEmployee = _employeeRepository.GetEmployeeById(_employee.EmployeeId);
            if (latestEmployee != null)
            {
                _employee = latestEmployee;
            }

            _viewModel.WelcomeText = $"Welcome, {_employee.FullName} ({_username})";
            _viewModel.EmployeeCodeText = $"Code: {_employee.EmployeeCode}";
            _viewModel.PositionText = $"Position: {_employee.Position}";
            _viewModel.DepartmentText = $"Department: {_employee.Department}";
            _viewModel.HourlyRateText = $"Hourly Rate: PHP {_employee.HourlyRate:N2}";
            _viewModel.ProfileImage = _employee.ProfileImage;
        }

        private void LoadTodayAttendanceState()
        {
            var todayAttendance = _attendanceRepository.GetTodayAttendance(_employee.EmployeeId);
            if (todayAttendance == null)
            {
                _viewModel.AttendanceStatusText = "No attendance yet.";
                _viewModel.TimeInText = "-";
                _viewModel.TimeOutText = "-";
                _viewModel.ClockActionButtonText = "Open Attendance";
                _viewModel.IsClockActionEnabled = true;
                return;
            }

            if (LeavePolicies.IsLeaveAttendanceStatus(todayAttendance.Status))
            {
                _viewModel.AttendanceStatusText = todayAttendance.Status;
                _viewModel.TimeInText = "-";
                _viewModel.TimeOutText = "-";
                _viewModel.ClockActionButtonText = "Leave Scheduled";
                _viewModel.IsClockActionEnabled = false;
                return;
            }

            _viewModel.TimeInText = todayAttendance.TimeIn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            _viewModel.TimeOutText = todayAttendance.TimeOut?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

            if (!todayAttendance.TimeOut.HasValue)
            {
                _viewModel.AttendanceStatusText = "Clocked in.";
                _viewModel.ClockActionButtonText = "Open Attendance";
                _viewModel.IsClockActionEnabled = true;
            }
            else
            {
                _viewModel.AttendanceStatusText = "Attendance complete.";
                _viewModel.ClockActionButtonText = "Open Attendance";
                _viewModel.IsClockActionEnabled = true;
            }
        }

        private void LoadAttendanceHistory()
        {
            _viewModel.AttendanceHistory.Clear();
            var records = _attendanceRepository.GetAttendanceByEmployee(
                _employee.EmployeeId,
                DateTime.Today.AddMonths(-3),
                DateTime.Today);

            foreach (var record in records)
            {
                _viewModel.AttendanceHistory.Add(record);
            }
        }

        private void LoadPayrollHistory()
        {
            _viewModel.PayrollHistory.Clear();
            var records = _payrollRepository.GetPayrollByEmployee(_employee.EmployeeId);
            foreach (var payroll in records)
            {
                _viewModel.PayrollHistory.Add(payroll);
            }

            var latest = records.FirstOrDefault();
            _viewModel.LatestPayrollText = latest == null
                ? "No payroll records yet."
                : $"{latest.PayPeriodStart:yyyy-MM-dd} to {latest.PayPeriodEnd:yyyy-MM-dd} | Net Pay: PHP {latest.NetPay:N2} ({latest.Status})";
        }

        private void LoadLeaveRequests()
        {
            _viewModel.LeaveRequests.Clear();
            var records = _leaveRequestRepository.GetLeaveRequestsByEmployee(_employee.EmployeeId);
            foreach (var leaveRequest in records)
            {
                _viewModel.LeaveRequests.Add(leaveRequest);
            }

            var latest = records.FirstOrDefault();
            _viewModel.LatestLeaveText = latest == null
                ? "No leave requests yet."
                : $"{latest.LeaveType} | {latest.StartDate:yyyy-MM-dd} to {latest.EndDate:yyyy-MM-dd} ({latest.Status})";

            _selectedLeaveRequest = null;
            _viewModel.CanCancelSelectedLeave = false;
            if (LeaveRequestsDataGrid.SelectedItem != null)
            {
                LeaveRequestsDataGrid.SelectedItem = null;
            }
        }

        private void FileLeave_Click(object sender, RoutedEventArgs e)
        {
            var modal = new LeaveRequestModal(_employee)
            {
                Owner = this
            };

            if (modal.ShowDialog() != true || modal.ResultLeaveRequest == null)
            {
                return;
            }

            try
            {
                _leaveRequestRepository.SubmitLeaveRequest(modal.ResultLeaveRequest);
                LoadLeaveRequests();
                MessageBox.Show(
                    "Leave request filed successfully. It is now waiting for admin approval.",
                    "Leave Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to file leave request.\n{ex.Message}",
                    "Leave Request Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LeaveRequestsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _selectedLeaveRequest = LeaveRequestsDataGrid.SelectedItem as LeaveRequest;
            _viewModel.CanCancelSelectedLeave = _selectedLeaveRequest?.CanEmployeeCancel == true;
        }

        private void CancelSelectedLeave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedLeaveRequest == null)
            {
                MessageBox.Show(
                    "Select a leave request to cancel.",
                    "Leave Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!_selectedLeaveRequest.CanEmployeeCancel)
            {
                MessageBox.Show(
                    "Only pending leave requests can be cancelled.",
                    "Leave Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Cancel leave request for {_selectedLeaveRequest.StartDate:yyyy-MM-dd} to {_selectedLeaveRequest.EndDate:yyyy-MM-dd}?",
                "Cancel Leave Request",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _leaveRequestRepository.CancelLeaveRequest(_selectedLeaveRequest.LeaveRequestId);
                LoadLeaveRequests();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to cancel leave request.\n{ex.Message}",
                    "Leave Request Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
