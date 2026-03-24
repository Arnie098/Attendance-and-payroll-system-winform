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
        private readonly EmployeeDashboardViewModel _viewModel = new();
        private Employee _employee;

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
    }
}
