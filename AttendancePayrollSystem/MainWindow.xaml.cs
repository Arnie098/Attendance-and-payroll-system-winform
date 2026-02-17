using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly AdminDashboardViewModel _dashboardViewModel;
        private readonly EmployeeRepository _employeeRepo = new();
        private readonly AttendanceRepository _attendanceRepo = new();

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            _dashboardViewModel = new AdminDashboardViewModel();
            DataContext = _viewModel;
            AdminDashboardTab.DataContext = _dashboardViewModel;
            LoadEmployees();
        }

        private void LoadEmployees()
        {
            _viewModel.LoadEmployees();
            EmployeeDataGrid.ItemsSource = _viewModel.Employees;
        }

        private void RefreshEmployees_Click(object sender, RoutedEventArgs e)
        {
            LoadEmployees();
            _dashboardViewModel.RefreshDashboard();
        }

        private void AddEmployee_Click(object sender, RoutedEventArgs e)
        {
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

            var target = _viewModel.SelectedEmployee;
            var confirm = MessageBox.Show(
                $"Delete employee {target.FullName} ({target.EmployeeCode})?\nThis will also remove related attendance and payroll records.",
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
    }
}
