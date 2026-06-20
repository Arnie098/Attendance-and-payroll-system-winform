using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem
{
    public partial class EmployeeManagementWindow : Window
    {
        private readonly MainViewModel _viewModel = new();
        private readonly EmployeeRepository _employeeRepo = new();
        private readonly AttendanceRepository _attendanceRepo = new();
        private readonly AuthRepository _authRepository = new();
        private readonly SchoolTeacherSyncService _schoolTeacherSyncService = new();
        private System.Collections.Generic.List<Attendance> _allAttendances = new();
        private readonly int? _initialEmployeeId;
        private readonly bool _promptForInitialSelection;
        private bool _hasPromptedForInitialSelection;

        public EmployeeManagementWindow(int? initialEmployeeId = null, bool promptForInitialSelection = true)
        {
            InitializeComponent();
            _initialEmployeeId = initialEmployeeId;
            _promptForInitialSelection = promptForInitialSelection;
            DataContext = _viewModel;
            Loaded += EmployeeManagementWindow_Loaded;
        }

        private async void EmployeeManagementWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= EmployeeManagementWindow_Loaded;
                await LoadEmployeesAsync();

                if (_initialEmployeeId.HasValue)
                {
                    SelectEmployeeById(_initialEmployeeId.Value);
                return;
            }

            if (_promptForInitialSelection)
            {
                await PromptForInitialEmployeeSelectionAsync();
            }
        }

        private async Task LoadEmployeesAsync()
        {
            var selectedEmployeeId = _viewModel.SelectedEmployee?.EmployeeId;
            var employees = await Task.Run(() => _employeeRepo.GetAllEmployees());
            _viewModel.ReplaceEmployees(employees);
            RestoreSelectedEmployee(selectedEmployeeId);
            UpdateEmployeeManagementState();
        }

        private async void RefreshEmployees_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await Task.Run(() => TrySynchronizeSchoolEmployees(showError: false));
                await LoadEmployeesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to refresh employees.\n{ex.Message}",
                    "Employee Management",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void FindEmployee_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (_viewModel.Employees.Count == 0)
                {
                    await LoadEmployeesAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load employees for search.\n{ex.Message}",
                    "Employee Search",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            ShowEmployeeSearchDialog();
        }

        private async void AddEmployee_Click(object sender, RoutedEventArgs e)
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
                Mouse.OverrideCursor = Cursors.Wait;
                var employee = modal.ResultEmployee;
                var newEmployeeId = await Task.Run(() =>
                {
                    var employeeId = _employeeRepo.AddEmployee(employee);
                    TrySynchronizeEmployeeAccounts(showError: false);
                    return employeeId;
                });

                await LoadEmployeesAsync();
                SelectEmployeeById(newEmployeeId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add employee.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void EditEmployee_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedEmployee == null)
            {
                return;
            }

            var isSchoolManaged = EmployeeSourcePolicy.IsSchoolManagedEmployee(_viewModel.SelectedEmployee);
            if (EmployeeSourcePolicy.UseSchoolAsExclusiveSource && !isSchoolManaged)
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
                Mouse.OverrideCursor = Cursors.Wait;
                var employee = modal.ResultEmployee;
                await Task.Run(() =>
                {
                    if (EmployeeSourcePolicy.UseSchoolAsExclusiveSource && isSchoolManaged)
                    {
                        _employeeRepo.UpdateAdminManagedFields(employee);
                    }
                    else
                    {
                        _employeeRepo.UpdateEmployee(employee);
                        TrySynchronizeEmployeeAccounts(showError: false);
                    }
                });

                await LoadEmployeesAsync();
                SelectEmployeeById(employee.EmployeeId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update employee.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void DeleteEmployee_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedEmployee == null)
            {
                return;
            }

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

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await Task.Run(() => _employeeRepo.DeleteEmployee(target.EmployeeId));
                await LoadEmployeesAsync();
                _viewModel.SelectedEmployee = null;
                AttendanceDataGrid.ItemsSource = null;
                UpdateEmployeeManagementState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete employee.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OpenAttendanceModal_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedEmployee == null)
            {
                return;
            }

            var modal = new AttendanceModal(_viewModel.SelectedEmployee)
            {
                Owner = this
            };

            modal.ShowDialog();
            await LoadEmployeeAttendanceAsync(_viewModel.SelectedEmployee.EmployeeId);
        }

        private void OpenPayrollModal_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedEmployee == null)
            {
                return;
            }

            var modal = new PayrollModal(_viewModel.SelectedEmployee)
            {
                Owner = this
            };

            modal.ShowDialog();
        }

        private async Task LoadEmployeeAttendanceAsync(int employeeId)
        {
            var attendances = await Task.Run(() => _attendanceRepo.GetAttendanceByEmployee(employeeId));
            if (_viewModel.SelectedEmployee?.EmployeeId == employeeId)
            {
                _allAttendances = attendances;
                AttendanceDataGrid.ItemsSource = attendances;
                if (AttendanceSearchBox != null) AttendanceSearchBox.Text = string.Empty;
            }
        }

        private void SelectEmployeeById(int employeeId)
        {
            var employee = _viewModel.Employees.FirstOrDefault(item => item.EmployeeId == employeeId);
            if (employee == null)
            {
                return;
            }

            _viewModel.SelectedEmployee = employee;
            _ = LoadSelectedEmployeeAttendanceAsync(employee.EmployeeId);
            UpdateEmployeeManagementState();
        }

        private async Task PromptForInitialEmployeeSelectionAsync()
        {
            if (_hasPromptedForInitialSelection || _viewModel.Employees.Count == 0)
            {
                return;
            }

            _hasPromptedForInitialSelection = true;
            await Task.Yield();
            ShowEmployeeSearchDialog();
        }

        private void ShowEmployeeSearchDialog()
        {
            var dialog = new EmployeeSearchWindow(_viewModel.Employees, _viewModel.SelectedEmployee?.EmployeeId)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.SelectedEmployeeId.HasValue)
            {
                SelectEmployeeById(dialog.SelectedEmployeeId.Value);
            }
        }

        private async Task LoadSelectedEmployeeAttendanceAsync(int employeeId)
        {
            try
            {
                await LoadEmployeeAttendanceAsync(employeeId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load attendance records.\n{ex.Message}",
                    "Attendance",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RestoreSelectedEmployee(int? employeeId)
        {
            if (!employeeId.HasValue)
            {
                return;
            }

            SelectEmployeeById(employeeId.Value);
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

        private void TrySynchronizeSchoolEmployees(bool showError = true)
        {
            try
            {
                _schoolTeacherSyncService.SyncTeachers();
                _authRepository.EnsureEmployeeAccounts();
            }
            catch (Exception ex)
            {
                if (!showError)
                {
                    return;
                }

                MessageBox.Show(
                    $"School employees could not be synchronized.\n{ex.Message}",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void UpdateEmployeeManagementState()
        {
            var usesSchoolSource = EmployeeSourcePolicy.UseSchoolAsExclusiveSource;
            var hasSelection = _viewModel.SelectedEmployee != null;
            var selectedIsSchoolManaged = EmployeeSourcePolicy.IsSchoolManagedEmployee(_viewModel.SelectedEmployee);
            AddEmployeeButton.IsEnabled = !usesSchoolSource;
            OpenPayrollHeaderButton.IsEnabled = hasSelection;
            EditEmployeeButton.IsEnabled = hasSelection && (!usesSchoolSource || selectedIsSchoolManaged);
            DeleteEmployeeButton.IsEnabled = !usesSchoolSource && hasSelection;

            var infoMessage = string.Empty;
            if (usesSchoolSource && selectedIsSchoolManaged)
            {
                infoMessage = EmployeeSourcePolicy.LinkedEmployeeEditMessage;
            }
            else if (usesSchoolSource)
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AttendanceSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = AttendanceSearchBox.Text.Trim();
            if (_viewModel.SelectedEmployee == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(searchText))
            {
                AttendanceDataGrid.ItemsSource = _allAttendances;
                return;
            }

            var filtered = _allAttendances
                .Where(att =>
                    att.AttendanceDate.ToString("yyyy-MM-dd").Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    att.Status.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            AttendanceDataGrid.ItemsSource = filtered;
        }
    }
}
