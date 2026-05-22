using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
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
            ReplaceEmployees(_employeeRepo.GetAllEmployees());
        }

        public async Task LoadEmployeesAsync()
        {
            var employees = await Task.Run(() => _employeeRepo.GetAllEmployees());
            ReplaceEmployees(employees);
        }

        public void ReplaceEmployees(IEnumerable<Employee> employees)
        {
            Employees.Clear();
            foreach (var employee in employees)
            {
                Employees.Add(employee);
            }
        }
    }
}
