using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly AdminDashboardViewModel _dashboardViewModel;
        private readonly EmployeeRepository _employeeRepo = new();
        private readonly AttendanceRepository _attendanceRepo = new();
        private readonly LeaveRequestRepository _leaveRequestRepository = new();
        private readonly AuthRepository _authRepository = new();
        private readonly SchoolTeacherSyncService _schoolTeacherSyncService = new();
        private readonly string _currentUsername;
        private LeaveRequest? _selectedLeaveRequest;

        public MainWindow(string currentUsername = "admin")
        {
            InitializeComponent();
            _currentUsername = string.IsNullOrWhiteSpace(currentUsername) ? "admin" : currentUsername.Trim();
            _viewModel = new MainViewModel();
            _dashboardViewModel = new AdminDashboardViewModel();
            DataContext = _viewModel;
            AdminDashboardTab.DataContext = _dashboardViewModel;
            TrySynchronizeRuntime(false);
            LoadEmployees();
        }

        private void LoadEmployees()
        {
            _viewModel.LoadEmployees();
            EmployeeDataGrid.ItemsSource = _viewModel.Employees;
            LoadLeaveRequests();
            UpdateEmployeeManagementState();
        }

        public void RefreshRuntimeData()
        {
            TrySynchronizeRuntime(true);
            LoadEmployees();
            _dashboardViewModel.RefreshDashboard();
        }

        public void NavigateToEmployeeManagement()
        {
            MainTabControl.SelectedItem = EmployeeManagementTabItem;
        }

        public void NavigateToDashboard()
        {
            MainTabControl.SelectedItem = DashboardTabItem;
        }

        public void LogoutToLogin()
        {
            Logout_Click(this, new RoutedEventArgs());
        }

        private void RefreshEmployees_Click(object sender, RoutedEventArgs e)
        {
            RefreshRuntimeData();
        }

        private void AddEmployee_Click(object sender, RoutedEventArgs e)
        {
            if (EmployeeSourcePolicy.UseSchoolAsExclusiveSource)
            {
                ShowSchoolEmployeeManagementMessage();
                return;
            }

            var modal = new EmployeeModal
            {
                Owner = this
            };

            if (modal.ShowDialog() != true || modal.ResultEmployee == null)
            {
                return;
            }

            try
            {
                var newEmployeeId = _employeeRepo.AddEmployee(modal.ResultEmployee);
                TrySynchronizeEmployeeAccounts();
                LoadEmployees();
                SelectEmployeeById(newEmployeeId);
                _dashboardViewModel.RefreshDashboard();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add employee.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditEmployee_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedEmployee == null) return;

            if (EmployeeSourcePolicy.UseSchoolAsExclusiveSource)
            {
                ShowSchoolEmployeeManagementMessage();
                return;
            }

            var modal = new EmployeeModal(_viewModel.SelectedEmployee)
            {
                Owner = this
            };

            if (modal.ShowDialog() != true || modal.ResultEmployee == null)
            {
                return;
            }

            try
            {
                _employeeRepo.UpdateEmployee(modal.ResultEmployee);
                TrySynchronizeEmployeeAccounts();
                LoadEmployees();
                SelectEmployeeById(modal.ResultEmployee.EmployeeId);
                _dashboardViewModel.RefreshDashboard();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update employee.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteEmployee_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedEmployee == null) return;

            if (EmployeeSourcePolicy.UseSchoolAsExclusiveSource)
            {
                ShowSchoolEmployeeManagementMessage();
                return;
            }

            var target = _viewModel.SelectedEmployee;
            var isSchoolManaged = EmployeeSourcePolicy.IsSchoolManagedEmployee(target);
            var confirm = MessageBox.Show(
                isSchoolManaged
                    ? $"Delete employee {target.FullName} ({target.EmployeeCode})?\n{EmployeeSourcePolicy.LinkedEmployeeDeleteMessage}\n\nThis will also remove related leave, attendance, and payroll records."
                    : $"Delete employee {target.FullName} ({target.EmployeeCode})?\nThis will also remove related leave, attendance, and payroll records.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                _employeeRepo.DeleteEmployee(target.EmployeeId);
                LoadEmployees();
                EmployeeDataGrid.SelectedItem = null;
                _viewModel.SelectedEmployee = null;
                AttendanceDataGrid.ItemsSource = null;
                _dashboardViewModel.RefreshDashboard();
                UpdateEmployeeManagementState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete employee.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EmployeeDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EmployeeDataGrid.SelectedItem is Employee employee)
            {
                _viewModel.SelectedEmployee = employee;
                LoadEmployeeAttendance(employee.EmployeeId);
            }
            else
            {
                _viewModel.SelectedEmployee = null;
                AttendanceDataGrid.ItemsSource = null;
            }

            UpdateEmployeeManagementState();
        }

        private void OpenAttendanceModal_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedEmployee == null) return;

            var modal = new AttendanceModal(_viewModel.SelectedEmployee)
            {
                Owner = this
            };
            modal.ShowDialog();
            LoadEmployeeAttendance(_viewModel.SelectedEmployee.EmployeeId);
            LoadLeaveRequests();
            _dashboardViewModel.RefreshDashboard();
        }

        private void OpenPayrollModal_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedEmployee == null) return;

            var modal = new PayrollModal(_viewModel.SelectedEmployee)
            {
                Owner = this
            };
            modal.ShowDialog();
            _dashboardViewModel.RefreshDashboard();
        }

        private void LoadEmployeeAttendance(int employeeId)
        {
            var attendances = _attendanceRepo.GetAttendanceByEmployee(employeeId);
            AttendanceDataGrid.ItemsSource = attendances;
        }

        private void SelectEmployeeById(int employeeId)
        {
            var employee = _viewModel.Employees.FirstOrDefault(e => e.EmployeeId == employeeId);
            if (employee == null) return;

            EmployeeDataGrid.SelectedItem = employee;
            EmployeeDataGrid.ScrollIntoView(employee);
        }

        private void LoadLeaveRequests()
        {
            var selectedLeaveRequestId = _selectedLeaveRequest?.LeaveRequestId;
            var requests = _leaveRequestRepository.GetLeaveRequests();
            LeaveRequestsDataGrid.ItemsSource = requests;

            if (!selectedLeaveRequestId.HasValue)
            {
                ResetLeaveRequestReviewPanel();
                return;
            }

            var refreshedSelection = requests.FirstOrDefault(request => request.LeaveRequestId == selectedLeaveRequestId.Value);
            if (refreshedSelection == null)
            {
                ResetLeaveRequestReviewPanel();
                return;
            }

            LeaveRequestsDataGrid.SelectedItem = refreshedSelection;
            LeaveRequestsDataGrid.ScrollIntoView(refreshedSelection);
        }

        private void RefreshLeaveRequests_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadLeaveRequests();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load leave requests.\n{ex.Message}",
                    "Leave Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LeaveRequestsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshLeaveRequestReviewPanel(LeaveRequestsDataGrid.SelectedItem as LeaveRequest);
        }

        private void ApproveLeaveRequest_Click(object sender, RoutedEventArgs e)
        {
            ReviewSelectedLeaveRequest(approve: true);
        }

        private void RejectLeaveRequest_Click(object sender, RoutedEventArgs e)
        {
            ReviewSelectedLeaveRequest(approve: false);
        }

        private void ReviewSelectedLeaveRequest(bool approve)
        {
            if (_selectedLeaveRequest == null)
            {
                MessageBox.Show(
                    "Select a leave request first.",
                    "Leave Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!string.Equals(_selectedLeaveRequest.Status, LeavePolicies.StatusPending, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Only pending leave requests can be reviewed.",
                    "Leave Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var actionText = approve ? "approve" : "reject";
            var successText = approve ? "approved" : "rejected";
            var result = MessageBox.Show(
                $"Are you sure you want to {actionText} leave request #{_selectedLeaveRequest.LeaveRequestId} for {_selectedLeaveRequest.EmployeeName}?",
                $"Confirm {char.ToUpper(actionText[0])}{actionText[1..]}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var selectedEmployeeId = _viewModel.SelectedEmployee?.EmployeeId;
            var reviewerNotes = LeaveReviewNotesTextBox.Text.Trim();

            try
            {
                if (approve)
                {
                    _leaveRequestRepository.ApproveLeaveRequest(_selectedLeaveRequest.LeaveRequestId, _currentUsername, reviewerNotes);
                }
                else
                {
                    _leaveRequestRepository.RejectLeaveRequest(_selectedLeaveRequest.LeaveRequestId, _currentUsername, reviewerNotes);
                }

                LoadEmployees();
                if (selectedEmployeeId.HasValue)
                {
                    SelectEmployeeById(selectedEmployeeId.Value);
                }

                _dashboardViewModel.RefreshDashboard();
                MessageBox.Show(
                    $"Leave request {successText} successfully.",
                    "Leave Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to {actionText} leave request.\n{ex.Message}",
                    "Leave Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RefreshLeaveRequestReviewPanel(LeaveRequest? leaveRequest)
        {
            _selectedLeaveRequest = leaveRequest;
            if (leaveRequest == null)
            {
                ResetLeaveRequestReviewPanel();
                return;
            }

            SelectedLeaveEmployeeTextBlock.Text = $"{leaveRequest.EmployeeCode} - {leaveRequest.EmployeeName}";
            SelectedLeaveTypeTextBlock.Text = leaveRequest.LeaveType;
            SelectedLeaveDatesTextBlock.Text = $"{leaveRequest.StartDate:yyyy-MM-dd} to {leaveRequest.EndDate:yyyy-MM-dd}";
            SelectedLeaveStatusTextBlock.Text = leaveRequest.Status;
            SelectedLeavePaymentTextBlock.Text = $"{leaveRequest.PaymentLabel} ({LeavePolicies.GetAttendanceStatus(leaveRequest.IsPaid)})";
            SelectedLeaveChargeableDaysTextBlock.Text = leaveRequest.ChargeableDays.ToString();
            SelectedLeaveReviewedByTextBlock.Text = string.IsNullOrWhiteSpace(leaveRequest.ReviewedBy)
                ? "Pending review"
                : leaveRequest.ReviewedAt.HasValue
                    ? $"{leaveRequest.ReviewedBy} on {leaveRequest.ReviewedAt.Value:yyyy-MM-dd HH:mm}"
                    : leaveRequest.ReviewedBy;
            SelectedLeaveReasonTextBlock.Text = leaveRequest.Reason;
            SelectedLeaveReviewHistoryTextBlock.Text = BuildLeaveReviewHistoryText(leaveRequest);

            var canReview = string.Equals(leaveRequest.Status, LeavePolicies.StatusPending, StringComparison.OrdinalIgnoreCase);
            ApproveLeaveRequestButton.IsEnabled = canReview;
            RejectLeaveRequestButton.IsEnabled = canReview;
            LeaveReviewNotesTextBox.IsEnabled = canReview;
            LeaveReviewNotesTextBox.Text = canReview ? string.Empty : leaveRequest.ReviewerNotes;
        }

        private void ResetLeaveRequestReviewPanel()
        {
            _selectedLeaveRequest = null;
            SelectedLeaveEmployeeTextBlock.Text = "No leave request selected.";
            SelectedLeaveTypeTextBlock.Text = "-";
            SelectedLeaveDatesTextBlock.Text = "-";
            SelectedLeaveStatusTextBlock.Text = "-";
            SelectedLeavePaymentTextBlock.Text = "-";
            SelectedLeaveChargeableDaysTextBlock.Text = "-";
            SelectedLeaveReviewedByTextBlock.Text = "-";
            SelectedLeaveReasonTextBlock.Text = "Select a leave request from the list to review its details.";
            SelectedLeaveReviewHistoryTextBlock.Text = "No review activity yet.";
            LeaveReviewNotesTextBox.IsEnabled = false;
            LeaveReviewNotesTextBox.Text = string.Empty;
            ApproveLeaveRequestButton.IsEnabled = false;
            RejectLeaveRequestButton.IsEnabled = false;
        }

        private static string BuildLeaveReviewHistoryText(LeaveRequest leaveRequest)
        {
            if (string.IsNullOrWhiteSpace(leaveRequest.ReviewedBy) && string.IsNullOrWhiteSpace(leaveRequest.ReviewerNotes))
            {
                return "No review activity yet.";
            }

            if (string.IsNullOrWhiteSpace(leaveRequest.ReviewerNotes))
            {
                return string.IsNullOrWhiteSpace(leaveRequest.ReviewedBy)
                    ? "No reviewer notes recorded."
                    : leaveRequest.ReviewedAt.HasValue
                        ? $"{leaveRequest.ReviewedBy} reviewed this request on {leaveRequest.ReviewedAt.Value:yyyy-MM-dd HH:mm}."
                        : $"{leaveRequest.ReviewedBy} reviewed this request.";
            }

            if (string.IsNullOrWhiteSpace(leaveRequest.ReviewedBy))
            {
                return leaveRequest.ReviewerNotes;
            }

            return leaveRequest.ReviewedAt.HasValue
                ? $"{leaveRequest.ReviewedBy} reviewed this request on {leaveRequest.ReviewedAt.Value:yyyy-MM-dd HH:mm}.\n{leaveRequest.ReviewerNotes}"
                : $"{leaveRequest.ReviewedBy} reviewed this request.\n{leaveRequest.ReviewerNotes}";
        }

        private void TrySynchronizeEmployeeAccounts(bool showError = true)
        {
            try
            {
                _authRepository.EnsureEmployeeAccounts();
            }
            catch (Exception ex)
            {
                if (!showError)
                {
                    return;
                }

                MessageBox.Show(
                    $"Employee login accounts could not be synchronized.\n{ex.Message}",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void TrySynchronizeRuntime(bool showError = true)
        {
            try
            {
                MySqlOfflineSyncService.TrySynchronizeNow();
            }
            catch (Exception ex)
            {
                if (!showError)
                {
                    return;
                }

                MessageBox.Show(
                    $"Database synchronization could not be completed.\n{ex.Message}",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void TrySynchronizeSchoolTeachers(bool showError = true)
        {
            try
            {
                _schoolTeacherSyncService.SyncTeachers();
            }
            catch (Exception ex)
            {
                if (!showError)
                {
                    return;
                }

                MessageBox.Show(
                    $"School teacher sync could not be completed.\n{ex.Message}",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new LoginWindow();
            Application.Current.MainWindow = loginWindow;
            loginWindow.Show();
            Close();
        }

        private void UpdateEmployeeManagementState()
        {
            var usesSchoolSource = EmployeeSourcePolicy.UseSchoolAsExclusiveSource;
            var hasSelection = _viewModel.SelectedEmployee != null;
            AddEmployeeButton.IsEnabled = !usesSchoolSource;
            EditEmployeeButton.IsEnabled = !usesSchoolSource && hasSelection;
            DeleteEmployeeButton.IsEnabled = !usesSchoolSource && hasSelection;

            var infoMessage = string.Empty;
            if (usesSchoolSource)
            {
                infoMessage = EmployeeSourcePolicy.EmployeeManagementMessage;
            }
            else if (EmployeeSourcePolicy.IsSchoolManagedEmployee(_viewModel.SelectedEmployee))
            {
                infoMessage = EmployeeSourcePolicy.LinkedEmployeeEditMessage;
            }
            else if (EmployeeSourcePolicy.SchoolSyncEnabled)
            {
                infoMessage = EmployeeSourcePolicy.EmployeeManagementMessage;
            }

            EmployeeSourceInfoTextBlock.Text = infoMessage;
            EmployeeSourceInfoTextBlock.Visibility = string.IsNullOrWhiteSpace(infoMessage)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private static void ShowSchoolEmployeeManagementMessage()
        {
            MessageBox.Show(
                EmployeeSourcePolicy.EmployeeManagementMessage,
                "Employee Management Locked",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
