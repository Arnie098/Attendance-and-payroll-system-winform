using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem
{
    public partial class EmployeeSearchWindow : Window
    {
        private readonly List<Employee> _employees;
        private readonly ICollectionView _employeeView;

        public int? SelectedEmployeeId { get; private set; }

        public EmployeeSearchWindow(IEnumerable<Employee> employees, int? selectedEmployeeId = null)
        {
            InitializeComponent();
            _employees = employees.ToList();
            _employeeView = CollectionViewSource.GetDefaultView(_employees);
            _employeeView.Filter = FilterEmployee;
            EmployeeDataGrid.ItemsSource = _employeeView;

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
            UpdateSearchState();
            Loaded += EmployeeSearchWindow_Loaded;
        }

        private void EmployeeSearchWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= EmployeeSearchWindow_Loaded;
            SearchTextBox.Focus();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _employeeView.Refresh();
            ClearSelectionWhenFilteredOut();
            UpdateSearchState();
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;

                if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
                {
                    DialogResult = false;
                    Close();
                    return;
                }

                SearchTextBox.Clear();
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SelectFirstVisibleEmployeeIfNeeded();
                TrySelectEmployee();
            }
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            SearchTextBox.Focus();
        }

        private bool FilterEmployee(object item)
        {
            if (item is not Employee employee)
            {
                return false;
            }

            var searchText = NormalizeSearchText(SearchTextBox.Text);
            if (string.IsNullOrEmpty(searchText))
            {
                return true;
            }

            var searchTerms = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (searchTerms.Length == 0)
            {
                return true;
            }

            var searchableParts = new[]
            {
                employee.EmployeeId.ToString(CultureInfo.InvariantCulture),
                NormalizeSearchText(employee.EmployeeCode),
                NormalizeSearchText(employee.FullName),
                NormalizeSearchText(employee.Position),
                NormalizeSearchText(employee.Department),
                NormalizeSearchText(employee.Email),
                NormalizeSearchText(employee.Phone),
                employee.IsActive ? "active enabled current" : "inactive disabled"
            };
            var searchableText = string.Join(' ', searchableParts.Where(part => !string.IsNullOrWhiteSpace(part)));
            var compactSearchableText = RemoveSpaces(searchableText);

            return searchTerms.All(term => MatchesSearchTerm(employee, searchableText, compactSearchableText, term));
        }

        private void EmployeeDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectButtonState();
        }

        private void EmployeeDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) == null)
            {
                return;
            }

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

        private void UpdateSearchState()
        {
            var resultCount = _employeeView.Cast<Employee>().Count();
            var totalCount = _employees.Count;
            var hasSearch = !string.IsNullOrWhiteSpace(SearchTextBox.Text);

            ClearSearchButton.Visibility = hasSearch ? Visibility.Visible : Visibility.Collapsed;
            SearchResultCountTextBlock.Text = hasSearch
                ? $"{resultCount} of {totalCount} employees found"
                : $"{totalCount} employees";

            UpdateSelectButtonState();
        }

        private void ClearSelectionWhenFilteredOut()
        {
            if (EmployeeDataGrid.SelectedItem is not Employee selectedEmployee)
            {
                return;
            }

            if (!_employeeView.Cast<Employee>().Any(employee => employee.EmployeeId == selectedEmployee.EmployeeId))
            {
                EmployeeDataGrid.SelectedItem = null;
            }
        }

        private void SelectFirstVisibleEmployeeIfNeeded()
        {
            if (EmployeeDataGrid.SelectedItem is Employee)
            {
                return;
            }

            var firstVisibleEmployee = _employeeView.Cast<Employee>().FirstOrDefault();
            if (firstVisibleEmployee == null)
            {
                return;
            }

            EmployeeDataGrid.SelectedItem = firstVisibleEmployee;
            EmployeeDataGrid.ScrollIntoView(firstVisibleEmployee);
        }

        private static string NormalizeSearchText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var character in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
            }

            return string.Join(' ', builder
                .ToString()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string RemoveSpaces(string value)
        {
            return value.Replace(" ", string.Empty, StringComparison.Ordinal);
        }

        private static bool MatchesSearchTerm(Employee employee, string searchableText, string compactSearchableText, string term)
        {
            if (string.Equals(term, "active", StringComparison.OrdinalIgnoreCase)
                || string.Equals(term, "enabled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(term, "current", StringComparison.OrdinalIgnoreCase))
            {
                return employee.IsActive;
            }

            if (string.Equals(term, "inactive", StringComparison.OrdinalIgnoreCase)
                || string.Equals(term, "disabled", StringComparison.OrdinalIgnoreCase))
            {
                return !employee.IsActive;
            }

            var searchableTokens = searchableText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return searchableText.Contains(term, StringComparison.OrdinalIgnoreCase)
                || compactSearchableText.Contains(RemoveSpaces(term), StringComparison.OrdinalIgnoreCase)
                || searchableTokens.Any(token => token.StartsWith(term, StringComparison.OrdinalIgnoreCase));
        }

        private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T parent)
                {
                    return parent;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }
    }
}
