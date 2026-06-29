using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using Microsoft.Win32;

namespace AttendancePayrollSystem
{
    public partial class GenerateDtrWindow : Window
    {
        private readonly EmployeeRepository _employeeRepo = new();
        private readonly AttendanceRepository _attendanceRepo = new();
        private readonly AppBrandingRepository _brandingRepo = new();
        private readonly ObservableCollection<EmployeeDtrRow> _rows = new();

        public GenerateDtrWindow()
        {
            InitializeComponent();
            EmployeeGrid.ItemsSource = _rows;

            var today = DateTime.Today;
            StartDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
            EndDatePicker.SelectedDate = today;

            Loaded += async (_, _) => await LoadEmployeesAsync();
        }

        private async Task LoadEmployeesAsync()
        {
            var employees = await Task.Run(() => _employeeRepo.GetAllEmployees().Where(e => e.IsActive).ToList());
            _rows.Clear();
            foreach (var emp in employees.OrderBy(e => e.FullName))
                _rows.Add(new EmployeeDtrRow(emp) { IsSelected = true });

            UpdateCountLabels();
            EmployeeCountText.Text = $"{_rows.Count} active employees";
        }

        private void UpdateCountLabels()
        {
            var selected = _rows.Count(r => r.IsSelected);
            SelectedCountText.Text = $"{selected} selected";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsSelected = true;
            UpdateCountLabels();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsSelected = false;
            UpdateCountLabels();
        }

        private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool check = (sender as CheckBox)?.IsChecked == true;
            foreach (var r in _rows) r.IsSelected = check;
            UpdateCountLabels();
        }

        private void EmployeeCheckBox_Click(object sender, RoutedEventArgs e) => UpdateCountLabels();

        private (DateTime start, DateTime end)? GetPeriod()
        {
            if (!StartDatePicker.SelectedDate.HasValue || !EndDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Please select a start and end date.", "Period Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            var start = StartDatePicker.SelectedDate.Value;
            var end = EndDatePicker.SelectedDate.Value;
            if (start > end)
            {
                MessageBox.Show("Start date must be before end date.", "Invalid Period", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            return (start, end);
        }

        private List<EmployeeDtrRow> GetSelectedRows()
        {
            var selected = _rows.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0)
                MessageBox.Show("No employees selected.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return selected;
        }

        private async void PreviewSelected_Click(object sender, RoutedEventArgs e)
        {
            var period = GetPeriod();
            if (period == null) return;

            var row = EmployeeGrid.SelectedItem as EmployeeDtrRow
                      ?? _rows.FirstOrDefault(r => r.IsSelected);

            if (row == null)
            {
                MessageBox.Show("Select or check an employee to preview.", "No Employee", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await ShowPreviewAsync(row.Employee, period.Value.start, period.Value.end);
        }

        private async Task ShowPreviewAsync(Employee employee, DateTime start, DateTime end)
        {
            try
            {
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                var (attendances, branding) = await Task.Run(() =>
                    (_attendanceRepo.GetAttendanceByEmployee(employee.EmployeeId, start, end),
                     _brandingRepo.GetBranding()));

                Mouse.OverrideCursor = null;
                var win = new DtrPreviewWindow(employee, attendances, start, end, branding) { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show($"Failed to load preview.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void PrintAll_Click(object sender, RoutedEventArgs e)
        {
            var period = GetPeriod();
            if (period == null) return;
            var selected = GetSelectedRows();
            if (selected.Count == 0) return;

            var msg = $"This will open the print dialog once per employee ({selected.Count} total). Proceed?";
            if (MessageBox.Show(msg, "Print All DTR", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var branding = await Task.Run(() => _brandingRepo.GetBranding());
            SetProgress(0, selected.Count);

            int done = 0;
            foreach (var row in selected)
            {
                StatusText.Text = $"Printing {row.Employee.FullName}...";
                var attendances = await Task.Run(() =>
                    _attendanceRepo.GetAttendanceByEmployee(row.Employee.EmployeeId, period.Value.start, period.Value.end));

                DtrDocument.Print(row.Employee, attendances, period.Value.start, period.Value.end,
                    branding.CertifyingOfficerName, branding.CertifyingOfficerName, branding.CertifyingOfficerTitle);

                done++;
                SetProgress(done, selected.Count);
            }

            HideProgress();
            StatusText.Text = $"Printed {done} DTR(s).";
        }

        private async void SaveAllPdf_Click(object sender, RoutedEventArgs e)
        {
            var period = GetPeriod();
            if (period == null) return;
            var selected = GetSelectedRows();
            if (selected.Count == 0) return;

            var dialog = new OpenFolderDialog
            {
                Title = "Select folder to save DTR PDFs"
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
                return;

            var folder = dialog.FolderName;
            var branding = await Task.Run(() => _brandingRepo.GetBranding());
            SetProgress(0, selected.Count);

            int done = 0, failed = 0;
            var errors = new List<string>();

            foreach (var row in selected)
            {
                StatusText.Text = $"Saving {row.Employee.FullName}...";
                try
                {
                    var attendances = await Task.Run(() =>
                        _attendanceRepo.GetAttendanceByEmployee(row.Employee.EmployeeId, period.Value.start, period.Value.end));

                    var doc = await Application.Current.Dispatcher.InvokeAsync(() =>
                        DtrDocument.Build(row.Employee, attendances, period.Value.start, period.Value.end,
                            branding.CertifyingOfficerName, branding.CertifyingOfficerName, branding.CertifyingOfficerTitle));

                    doc.PageWidth = 816;
                    doc.PageHeight = 1056;
                    doc.PagePadding = new Thickness(48);
                    doc.ColumnWidth = 816 - 96;

                    var fileName = $"DTR_{SanitizeFileName(row.Employee.EmployeeCode)}_{period.Value.start:yyyy-MM}.pdf";
                    var filePath = Path.Combine(folder, fileName);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        SaveDocumentAsPdf(doc, filePath));

                    done++;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{row.Employee.FullName}: {ex.Message}");
                }

                SetProgress(done + failed, selected.Count);
            }

            HideProgress();
            StatusText.Text = $"Saved {done} PDF(s) to {folder}.";

            var resultMsg = $"Saved {done} DTR PDF(s) to:\n{folder}";
            if (failed > 0)
                resultMsg += $"\n\n{failed} failed:\n" + string.Join("\n", errors.Take(5));

            MessageBox.Show(resultMsg, "Done", MessageBoxButton.OK,
                failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        private void SaveDocumentAsPdf(FlowDocument doc, string filePath)
        {
            using var server = new System.Printing.LocalPrintServer();
            var pdfQueue = server.GetPrintQueues(new[] { System.Printing.EnumeratedPrintQueueTypes.Local, System.Printing.EnumeratedPrintQueueTypes.Connections })
                .FirstOrDefault(q =>
                    string.Equals(q.FullName, "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(q.Name, "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase));

            if (pdfQueue == null)
                throw new InvalidOperationException("\"Microsoft Print to PDF\" printer not found.");

            // The PDF printer uses its own save dialog — we redirect via XPS writer
            var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            paginator.PageSize = new Size(816, 1056);

            var xpsPath = Path.ChangeExtension(filePath, ".xps");
            var writer = System.Printing.PrintQueue.CreateXpsDocumentWriter(pdfQueue);
            writer.Write(paginator);
        }

        private void SetProgress(int done, int total)
        {
            ProgressPanel.Visibility = Visibility.Visible;
            GenerateProgress.Maximum = total;
            GenerateProgress.Value = done;
            ProgressText.Text = $"Processing {done} of {total}...";
        }

        private void HideProgress()
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
        }

        private static string SanitizeFileName(string name) =>
            string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }

    public class EmployeeDtrRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public EmployeeDtrRow(Employee employee) => Employee = employee;

        public Employee Employee { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
