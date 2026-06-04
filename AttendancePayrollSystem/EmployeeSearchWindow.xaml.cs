using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem
{
    public partial class EmployeeSearchWindow : Window
    {
        private readonly List<Employee> _employees;

        public int? SelectedEmployeeId { get; private set; }

        public EmployeeSearchWindow(IEnumerable<Employee> employees, int? selectedEmployeeId = null)
        {
            InitializeComponent();
            _employees = employees.ToList();
            EmployeeDataGrid.ItemsSource = _employees;

            if (selectedEmployeeId.HasValue)
            {
                var employee = _employees.FirstOrDefault(item => item.EmployeeId == selectedEmployeeId.Value);
                if (employee != null)
                {
                    EmployeeDataGrid.SelectedItem = employee;
                    EmployeeDataGrid.ScrollIntoView(employee);
                }
            }

            UpdateSelectButtonState();
            Loaded += EmployeeSearchWindow_Loaded;
        }

        private void EmployeeSearchWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= EmployeeSearchWindow_Loaded;
            SearchTextBox.Focus();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = SearchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(searchText))
            {
                EmployeeDataGrid.ItemsSource = _employees;
                return;
            }

            EmployeeDataGrid.ItemsSource = _employees
                .Where(employee =>
                    employee.FullName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    employee.EmployeeCode.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    (employee.Position ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    (employee.Department ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    (employee.Email ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void EmployeeDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectButtonState();
        }

        private void EmployeeDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            TrySelectEmployee();
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            TrySelectEmployee();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TrySelectEmployee()
        {
            if (EmployeeDataGrid.SelectedItem is not Employee employee)
            {
                return;
            }

            SelectedEmployeeId = employee.EmployeeId;
            DialogResult = true;
            Close();
        }

        private void UpdateSelectButtonState()
        {
            SelectButton.IsEnabled = EmployeeDataGrid?.SelectedItem is Employee;
        }
    }
}
