using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem
{
    public partial class EmployeeDashboardWindow : Window
    {
        private readonly string _username;
        private readonly AppBrandingRepository _appBrandingRepository = new();
        private readonly AttendanceRepository _attendanceRepository = new();
        private readonly PayrollRepository _payrollRepository = new();
        private readonly EmployeeRepository _employeeRepository = new();
        private readonly LeaveRequestRepository _leaveRequestRepository = new();
        private readonly LeaveDocumentRepository _leaveDocumentRepository = new();
        private readonly EmployeeDashboardViewModel _viewModel = new();
        private Employee _employee;
        private LeaveRequest? _selectedLeaveRequest;
        private bool _isLoadingDashboard;

        public EmployeeDashboardWindow(Employee employee, string username)
        {
            InitializeComponent();
            _employee = employee;
            _username = username;
            DataContext = _viewModel;
            Loaded += EmployeeDashboardWindow_Loaded;
        }

        private async void EmployeeDashboardWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= EmployeeDashboardWindow_Loaded;
            await LoadDashboardDataAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadDashboardDataAsync();
        }

        private async void ChangePhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ProfileImageFilePicker.TryPick(this, out var imageBytes))
                {
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                await Task.Run(() => _employeeRepository.UpdateProfileImage(_employee.EmployeeId, imageBytes));
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
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void RemovePhoto_Click(object sender, RoutedEventArgs e)
        {
            if (_employee.ProfileImage == null || _employee.ProfileImage.Length == 0)
            {
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await Task.Run(() => _employeeRepository.UpdateProfileImage(_employee.EmployeeId, null));
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
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void ClockAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var todayAttendance = await Task.Run(() => _attendanceRepository.GetTodayAttendance(_employee.EmployeeId));
                Mouse.OverrideCursor = null;
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
                await LoadDashboardDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open attendance terminal.\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new LoginWindow();
            Application.Current.MainWindow = loginWindow;
            loginWindow.Show();
            Close();
        }

        private async Task LoadDashboardDataAsync()
        {
            if (_isLoadingDashboard)
            {
                return;
            }

            try
            {
                _isLoadingDashboard = true;
                Mouse.OverrideCursor = Cursors.Wait;
                var employeeId = _employee.EmployeeId;
                var currentEmployee = _employee;
                var snapshot = await Task.Run(() =>
                {
                    var latestEmployee = _employeeRepository.GetEmployeeById(employeeId) ?? currentEmployee;
                    return new EmployeeDashboardSnapshot
                    {
                        LogoImage = TryLoadLogoImage(),
                        Employee = latestEmployee,
                        TodayText = DateTime.Now.ToString("MMMM dd, yyyy"),
                        TodayAttendance = _attendanceRepository.GetTodayAttendance(employeeId),
                        AttendanceHistory = _attendanceRepository.GetAttendanceByEmployee(
                            employeeId,
                            DateTime.Today.AddMonths(-3),
                            DateTime.Today),
                        PayrollHistory = _payrollRepository.GetPayrollByEmployee(employeeId),
                        LeaveRequests = _leaveRequestRepository.GetLeaveRequestsByEmployee(employeeId)
                    };
                });

                ApplyDashboardSnapshot(snapshot);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to refresh dashboard.\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isLoadingDashboard = false;
            }
        }

        private byte[]? TryLoadLogoImage()
        {
            try
            {
                return _appBrandingRepository.GetBranding().LogoImage;
            }
            catch
            {
                return null;
            }
        }

        private void ApplyDashboardSnapshot(EmployeeDashboardSnapshot snapshot)
        {
            BrandingVisualHelper.ApplyLogo(EmployeeBrandLogoImage, EmployeeBrandLogoFallbackPanel, snapshot.LogoImage);
            _employee = snapshot.Employee;

            _viewModel.TodayText = snapshot.TodayText;
            _viewModel.WelcomeText = $"Welcome, {_employee.FullName} ({_username})";
            _viewModel.EmployeeCodeText = $"Code: {_employee.EmployeeCode}";
            _viewModel.PositionText = $"Position: {_employee.Position}";
            _viewModel.DepartmentText = $"Department: {_employee.Department}";
            _viewModel.HourlyRateText = $"Hourly Rate: PHP {_employee.HourlyRate:N2}";
            _viewModel.ProfileImage = _employee.ProfileImage;

            ApplyTodayAttendanceState(snapshot.TodayAttendance);
            ApplyAttendanceHistory(snapshot.AttendanceHistory);
            ApplyPayrollHistory(snapshot.PayrollHistory);
            ApplyLeaveRequests(snapshot.LeaveRequests);
        }

        private void ApplyTodayAttendanceState(Attendance? todayAttendance)
        {
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

            // Show AM and PM session times
            var amIn = todayAttendance.TimeInAM?.ToString("hh:mm tt") ?? "-";
            var amOut = todayAttendance.TimeOutAM?.ToString("hh:mm tt") ?? "-";
            var pmIn = todayAttendance.TimeInPM?.ToString("hh:mm tt") ?? "-";
            var pmOut = todayAttendance.TimeOutPM?.ToString("hh:mm tt") ?? "-";

            _viewModel.TimeInText = $"AM: {amIn} | PM: {pmIn}";
            _viewModel.TimeOutText = $"AM: {amOut} | PM: {pmOut}";

            var sessionState = _attendanceRepository.GetSessionState(todayAttendance);
            if (sessionState == AttendanceSessionState.AllComplete)
            {
                _viewModel.AttendanceStatusText = "Attendance complete.";
                _viewModel.ClockActionButtonText = "Open Attendance";
                _viewModel.IsClockActionEnabled = true;
            }
            else
            {
                _viewModel.AttendanceStatusText = "In progress.";
                _viewModel.ClockActionButtonText = "Open Attendance";
                _viewModel.IsClockActionEnabled = true;
            }
        }

        private void ApplyAttendanceHistory(IEnumerable<Attendance> records)
        {
            _viewModel.SetAttendanceData(records);
        }

        private void ApplyPayrollHistory(IReadOnlyList<Payroll> records)
        {
            _viewModel.SetPayrollData(records);
        }

        // ── Attendance pagination handlers ──

        private void AttendancePrevious_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AttendancePreviousPage();
        }

        private void AttendanceNext_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AttendanceNextPage();
        }

        // ── Payroll pagination handlers ──

        private void PayrollPrevious_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.PayrollPreviousPage();
        }

        private void PayrollNext_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.PayrollNextPage();
        }

        private void ApplyLeaveRequests(IReadOnlyList<LeaveRequest> records)
        {
            _viewModel.LeaveRequests.Clear();
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

        private async void FileLeave_Click(object sender, RoutedEventArgs e)
        {
            var modal = new LeaveRequestModal(_employee)
            {
                Owner = this
            };

            if (modal.ShowDialog() != true || modal.ResultLeaveRequest == null)
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var leaveRequest = modal.ResultLeaveRequest;
                var drafts = modal.ResultDocuments;

                var leaveRequestId = await Task.Run(
                    () => _leaveRequestRepository.SubmitLeaveRequest(leaveRequest));

                if (drafts.Count > 0)
                {
                    await Task.Run(() =>
                    {
                        foreach (var draft in drafts)
                        {
                            _leaveDocumentRepository.AddDocument(new Models.LeaveDocument
                            {
                                LeaveRequestId = leaveRequestId,
                                DocumentName   = draft.Name,
                                DocumentData   = draft.Data,
                                FileSizeBytes  = draft.Data.LongLength,
                                UploadedAt     = DateTime.UtcNow
                            });
                        }
                    });
                }

                await LoadDashboardDataAsync();
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
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void ViewPayroll_Click(object sender, RoutedEventArgs e)
        {
            var modal = new PayrollModal(_employee, isAdminView: false)
            {
                Owner = this
            };

            modal.ShowDialog();
        }

        private void LeaveRequestsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _selectedLeaveRequest = LeaveRequestsDataGrid.SelectedItem as LeaveRequest;
            _viewModel.CanCancelSelectedLeave = _selectedLeaveRequest?.CanEmployeeCancel == true;
        }

        private async void CancelSelectedLeave_Click(object sender, RoutedEventArgs e)
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
                Mouse.OverrideCursor = Cursors.Wait;
                var leaveRequestId = _selectedLeaveRequest.LeaveRequestId;
                await Task.Run(() => _leaveRequestRepository.CancelLeaveRequest(leaveRequestId));
                await LoadDashboardDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to cancel leave request.\n{ex.Message}",
                    "Leave Request Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private sealed class EmployeeDashboardSnapshot
        {
            public byte[]? LogoImage { get; init; }
            public Employee Employee { get; init; } = new();
            public string TodayText { get; init; } = string.Empty;
            public Attendance? TodayAttendance { get; init; }
            public List<Attendance> AttendanceHistory { get; init; } = new();
            public List<Payroll> PayrollHistory { get; init; } = new();
            public List<LeaveRequest> LeaveRequests { get; init; } = new();
        }
    }
}
