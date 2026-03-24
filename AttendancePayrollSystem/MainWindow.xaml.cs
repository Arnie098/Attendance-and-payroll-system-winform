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
        private readonly AuthRepository _authRepository = new();
        private readonly SchoolTeacherSyncService _schoolTeacherSyncService = new();

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            _dashboardViewModel = new AdminDashboardViewModel();
            DataContext = _viewModel;
            AdminDashboardTab.DataContext = _dashboardViewModel;
            TrySynchronizeSchoolTeachers(false);
            TrySynchronizeEmployeeAccounts(false);
            LoadEmployees();
        }

        private void LoadEmployees()
        {
            _viewModel.LoadEmployees();
            EmployeeDataGrid.ItemsSource = _viewModel.Employees;
            UpdateEmployeeManagementState();
        }

        private void RefreshEmployees_Click(object sender, RoutedEventArgs e)
        {
            TrySynchronizeSchoolTeachers(true);
            TrySynchronizeEmployeeAccounts(false);
            LoadEmployees();
            _dashboardViewModel.RefreshDashboard();
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
                    ? $"Delete employee {target.FullName} ({target.EmployeeCode})?\n{EmployeeSourcePolicy.LinkedEmployeeDeleteMessage}\n\nThis will also remove related attendance and payroll records."
                    : $"Delete employee {target.FullName} ({target.EmployeeCode})?\nThis will also remove related attendance and payroll records.",
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
