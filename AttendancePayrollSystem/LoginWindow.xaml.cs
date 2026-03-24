using System;
using System.Windows;
using System.Windows.Input;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using System.Windows.Media;

namespace AttendancePayrollSystem
{
    public partial class LoginWindow : Window
    {
        private readonly AuthRepository _authRepository = new();
        private readonly EmployeeRepository _employeeRepository = new();
        private readonly SchoolTeacherSyncService _schoolTeacherSyncService = new();

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += LoginWindow_Loaded;
        }

        private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeDatabase();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            TryLogin();
        }

        private void DatabaseSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new DatabaseSettingsWindow
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() == true)
            {
                InitializeDatabase(showSuccessMessage: true);
                return;
            }

            UpdateDatabaseTarget();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryLogin();
            }
        }

        private void TryLogin()
        {
            SetStatus(string.Empty);

            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Password;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                SetStatus("Username and password are required.");
                return;
            }

            UserAccount? account;
            try
            {
                account = _authRepository.Authenticate(username, password);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to authenticate user.\n{ex.Message}",
                    "Database Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (account == null)
            {
                SetStatus("Invalid username or password.");
                PasswordBox.Clear();
                return;
            }

            if (!account.IsActive)
            {
                SetStatus("This account is inactive. Contact your administrator.");
                return;
            }

            if (string.Equals(account.Role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
            {
                OpenTargetWindow(new MainWindow());
                return;
            }

            if (!string.Equals(account.Role, UserRoles.Employee, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Unsupported account role.");
                return;
            }

            if (!account.EmployeeId.HasValue)
            {
                SetStatus("Employee account is not linked to a profile.");
                return;
            }

            Employee? employee;
            try
            {
                employee = _employeeRepository.GetEmployeeById(account.EmployeeId.Value);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load employee profile.\n{ex.Message}",
                    "Database Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (employee == null || !employee.IsActive)
            {
                SetStatus("Employee profile is inactive or missing.");
                return;
            }

            if (EmployeeSourcePolicy.UseSchoolAsExclusiveSource && !employee.SourceTeacherId.HasValue)
            {
                SetStatus("This employee is not managed by the school management database.");
                return;
            }

            OpenTargetWindow(new EmployeeDashboardWindow(employee, account.Username));
        }

        private void OpenTargetWindow(Window window)
        {
            Application.Current.MainWindow = window;
            window.Show();
            Close();
        }

        private void SetStatus(string message)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void InitializeDatabase(bool showSuccessMessage = false)
        {
            UpdateDatabaseTarget();
            SetStatus(string.Empty);

            try
            {
                _authRepository.EnsureAuthSchemaAndSeedDefaults();
                var schoolSyncStatus = TrySynchronizeSchoolTeachers(false);
                _authRepository.EnsureEmployeeAccounts();
                SetDatabaseReady(true);
                SetDatabaseStatus(
                    string.IsNullOrWhiteSpace(schoolSyncStatus)
                        ? $"Connection ready. {DatabaseHelper.GetConnectionSummary()}"
                        : $"Connection ready. {DatabaseHelper.GetConnectionSummary()}\n{schoolSyncStatus}",
                    isError: false);

                if (showSuccessMessage)
                {
                    MessageBox.Show(
                        "Database configuration saved successfully.",
                        "Database Settings",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                UsernameTextBox.Focus();
            }
            catch (Exception ex)
            {
                SetDatabaseReady(false);
                SetDatabaseStatus(
                    $"Cannot connect to the database.\nOpen Database Settings and update the server details.\n\n{ex.Message}",
                    isError: true);
            }
        }

        private string? TrySynchronizeSchoolTeachers(bool showError)
        {
            try
            {
                var result = _schoolTeacherSyncService.SyncTeachers();
                return result.WasSkipped ? null : result.ToSummary();
            }
            catch (Exception ex)
            {
                if (showError)
                {
                    MessageBox.Show(
                        $"School teacher sync failed.\n{ex.Message}",
                        "School Sync Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return $"School sync warning: {ex.Message}";
            }
        }

        private void UpdateDatabaseTarget()
        {
            var sourceLabel = DatabaseConnectionSettingsStore.HasSavedOverride()
                ? "Local laptop setting"
                : "App default setting";
            DatabaseTargetTextBlock.Text = $"{DatabaseHelper.GetConnectionSummary()} ({sourceLabel})";
        }

        private void SetDatabaseReady(bool isReady)
        {
            UsernameTextBox.IsEnabled = isReady;
            PasswordBox.IsEnabled = isReady;
            LoginButton.IsEnabled = isReady;
        }

        private void SetDatabaseStatus(string message, bool isError)
        {
            DatabaseStatusTextBlock.Text = message;
            DatabaseStatusTextBlock.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(185, 28, 28))
                : new SolidColorBrush(Color.FromRgb(21, 128, 61));
            DatabaseStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
