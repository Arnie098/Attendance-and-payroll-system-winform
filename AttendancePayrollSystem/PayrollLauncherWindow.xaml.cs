using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.ViewModels;

namespace AttendancePayrollSystem
{
    public partial class PayrollLauncherWindow : Window
    {
        private readonly MainViewModel _viewModel = new();
        private readonly EmployeeRepository _employeeRepository = new();
        private bool _isRefreshing;

        public PayrollLauncherWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            Loaded += PayrollLauncherWindow_Loaded;
        }

        private async void PayrollLauncherWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= PayrollLauncherWindow_Loaded;
            await RefreshEmployeesAsync();
        }

        private async Task RefreshEmployeesAsync()
        {
            if (_isRefreshing)
            {
                return;
            }

            try
            {
                _isRefreshing = true;
                Mouse.OverrideCursor = Cursors.Wait;
                var employees = await Task.Run(() => _employeeRepository.GetAllEmployees());
                _viewModel.ReplaceEmployees(employees);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load employees.\n{ex.Message}",
                    "Payroll Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isRefreshing = false;
            }
        }

        private async void RefreshEmployees_Click(object sender, RoutedEventArgs e)
        {
            await RefreshEmployeesAsync();
        }

        private void EmployeeDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.SelectedEmployee = EmployeeDataGrid.SelectedItem as Employee;
        }

        private void OpenPayroll_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedEmployee == null)
            {
                MessageBox.Show(
                    "Select an employee first.",
                    "Payroll Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var modal = new PayrollModal(_viewModel.SelectedEmployee)
            {
                Owner = this
            };

            modal.ShowDialog();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
