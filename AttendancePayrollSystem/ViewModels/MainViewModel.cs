using System.Collections.ObjectModel;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly EmployeeRepository _employeeRepo = new();
        private Employee? _selectedEmployee;
        private bool _isEmployeeSelected;

        public ObservableCollection<Employee> Employees { get; set; } = new();

        public Employee? SelectedEmployee
        {
            get => _selectedEmployee;
            set
            {
                SetProperty(ref _selectedEmployee, value);
                IsEmployeeSelected = value != null;
            }
        }

        public bool IsEmployeeSelected
        {
            get => _isEmployeeSelected;
            set => SetProperty(ref _isEmployeeSelected, value);
        }

        public void LoadEmployees()
        {
            Employees.Clear();
            var employees = _employeeRepo.GetAllEmployees();

            foreach (var employee in employees)
            {
                Employees.Add(employee);
            }
        }
    }
}
