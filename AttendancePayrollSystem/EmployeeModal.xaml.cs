using System;
using System.Globalization;
using System.Windows;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem
{
    public partial class EmployeeModal : Window
    {
        private readonly Employee? _existingEmployee;

        public Employee? ResultEmployee { get; private set; }

        public EmployeeModal(Employee? employee = null)
        {
            InitializeComponent();
            _existingEmployee = employee;
            TitleText.Text = employee == null ? "Add Employee" : "Edit Employee";
            LoadEmployeeData();
            ConfigureSchoolLinkedState();
        }

        private void LoadEmployeeData()
        {
            if (_existingEmployee == null)
            {
                HireDatePicker.SelectedDate = DateTime.Today;
                IsActiveCheckBox.IsChecked = true;
                return;
            }

            EmployeeCodeTextBox.Text = _existingEmployee.EmployeeCode;
            FullNameTextBox.Text = _existingEmployee.FullName;
            EmailTextBox.Text = _existingEmployee.Email;
            PhoneTextBox.Text = _existingEmployee.Phone;
            PositionTextBox.Text = _existingEmployee.Position;
            DepartmentTextBox.Text = _existingEmployee.Department;
            HourlyRateTextBox.Text = _existingEmployee.HourlyRate.ToString("0.##", CultureInfo.InvariantCulture);
            HireDatePicker.SelectedDate = _existingEmployee.HireDate;
            IsActiveCheckBox.IsChecked = _existingEmployee.IsActive;
        }

        private void ConfigureSchoolLinkedState()
        {
            var isSchoolManaged = EmployeeSourcePolicy.IsSchoolManagedEmployee(_existingEmployee);
            SchoolLinkedInfoBorder.Visibility = isSchoolManaged ? Visibility.Visible : Visibility.Collapsed;
            SchoolLinkedInfoTextBlock.Text = isSchoolManaged
                ? EmployeeSourcePolicy.LinkedEmployeeEditMessage
                : string.Empty;

            EmployeeCodeTextBox.IsReadOnly = isSchoolManaged;
            FullNameTextBox.IsReadOnly = isSchoolManaged;
            EmailTextBox.IsReadOnly = isSchoolManaged;
            PhoneTextBox.IsReadOnly = isSchoolManaged;
            HireDatePicker.IsEnabled = !isSchoolManaged;
            IsActiveCheckBox.IsEnabled = !isSchoolManaged;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EmployeeCodeTextBox.Text) ||
                string.IsNullOrWhiteSpace(FullNameTextBox.Text))
            {
                MessageBox.Show("Employee code and full name are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(HourlyRateTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var hourlyRate) ||
                hourlyRate < 0)
            {
                MessageBox.Show("Please enter a valid hourly rate.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (HireDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Please select a hire date.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultEmployee = new Employee
            {
                EmployeeId = _existingEmployee?.EmployeeId ?? 0,
                EmployeeCode = EmployeeCodeTextBox.Text.Trim(),
                FullName = FullNameTextBox.Text.Trim(),
                Email = EmailTextBox.Text.Trim(),
                Phone = PhoneTextBox.Text.Trim(),
                Position = PositionTextBox.Text.Trim(),
                Department = DepartmentTextBox.Text.Trim(),
                HourlyRate = hourlyRate,
                HireDate = HireDatePicker.SelectedDate.Value,
                IsActive = IsActiveCheckBox.IsChecked ?? true,
                SourceTeacherId = _existingEmployee?.SourceTeacherId,
                SourceUserId = _existingEmployee?.SourceUserId,
                ProfileImage = _existingEmployee?.ProfileImage,
                BiometricTemplate = _existingEmployee?.BiometricTemplate
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
