using System;
using System.Threading.Tasks;
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
        private readonly AppBrandingRepository _appBrandingRepository = new();
        private readonly EmployeeRepository _employeeRepository = new();
        private readonly SchoolTeacherSyncService _schoolTeacherSyncService = new();
        private bool _isDatabaseReady;
        private bool _isInitializingDatabase;
        private bool _isAuthenticating;

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += LoginWindow_Loaded;
        }

        private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeDatabaseAsync();
            SyncToggleToCurrentState();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await TryLoginAsync();
        }

        private async void DatabaseSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializingDatabase || _isAuthenticating)
            {
                return;
            }

            var settingsWindow = new DatabaseSettingsWindow
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() == true)
            {
                await InitializeDatabaseAsync(showSuccessMessage: true);
                return;
            }

            UpdateDatabaseTarget();
        }

        private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await TryLoginAsync();
            }
        }

        private async Task TryLoginAsync()
        {
            if (_isInitializingDatabase || _isAuthenticating)
            {
                return;
            }

            SetStatus(string.Empty);

            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Password;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                SetStatus("Username and password are required.");
                return;
            }

            _isAuthenticating = true;
            UpdateInteractiveState();

            try
            {
                var account = await Task.Run(() => _authRepository.Authenticate(username, password));

                if (account == null)
                {
                    AppLogger.Auth($"Failed login attempt for username '{username}'.");
                    SetStatus("Invalid username or password.");
                    PasswordBox.Clear();
                    return;
                }

                if (!account.IsActive)
                {
                    AppLogger.Auth($"Inactive account login attempt: '{username}'.");
                    SetStatus("This account is inactive. Contact your administrator.");
                    return;
                }

                if (string.Equals(account.Role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Auth($"Admin login successful: '{account.Username}'.");
                    SetStatus("Opening admin dashboard...");
                    await Task.Yield();
                    OpenTargetWindow(new MainWindow(account.Username));
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

                var employee = await Task.Run(() => _employeeRepository.GetEmployeeById(account.EmployeeId.Value));

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

                SetStatus("Opening employee dashboard...");
                await Task.Yield();
                AppLogger.Auth($"Employee login successful: '{account.Username}' (EmployeeId={employee.EmployeeId}).");
                OpenTargetWindow(new EmployeeDashboardWindow(employee, account.Username));
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Login failed");
                MessageBox.Show(
                    $"Failed to sign in.\n{ex.Message}",
                    "Database Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isAuthenticating = false;
                UpdateInteractiveState();
            }
        }

        private async void DatabaseModeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingDatabase || _isAuthenticating)
            {
                // Revert the toggle without re-triggering the event
                DatabaseModeToggle.Checked -= DatabaseModeToggle_Changed;
                DatabaseModeToggle.Unchecked -= DatabaseModeToggle_Changed;
                SyncToggleToCurrentState();
                DatabaseModeToggle.Checked += DatabaseModeToggle_Changed;
                DatabaseModeToggle.Unchecked += DatabaseModeToggle_Changed;
                return;
            }

            var useOffline = DatabaseModeToggle.IsChecked == true;

            if (useOffline && !DatabaseRuntimeState.IsOfflineDatabaseAvailable)
            {
                MessageBox.Show(
                    "The offline database is not available. Please configure ATTENDANCE_OFFLINE_DB_CONNECTION in your .env file and ensure the local MySQL server is running.",
                    "Offline Database Unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                // Revert toggle
                DatabaseModeToggle.Checked -= DatabaseModeToggle_Changed;
                DatabaseModeToggle.Unchecked -= DatabaseModeToggle_Changed;
                DatabaseModeToggle.IsChecked = false;
                DatabaseModeToggle.Checked += DatabaseModeToggle_Changed;
                DatabaseModeToggle.Unchecked += DatabaseModeToggle_Changed;
                UpdateToggleLabels();
                return;
            }

            if (!useOffline && !DatabaseRuntimeState.IsOnlineAvailable)
            {
                MessageBox.Show(
                    "The online database is not reachable. Please check your internet connection or database settings.",
                    "Online Database Unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                // Revert toggle
                DatabaseModeToggle.Checked -= DatabaseModeToggle_Changed;
                DatabaseModeToggle.Unchecked -= DatabaseModeToggle_Changed;
                DatabaseModeToggle.IsChecked = true;
                DatabaseModeToggle.Checked += DatabaseModeToggle_Changed;
                DatabaseModeToggle.Unchecked += DatabaseModeToggle_Changed;
                UpdateToggleLabels();
                return;
            }

            // Apply the mode change
            DatabaseRuntimeState.SetRuntimeState(
                useOfflineDatabase: useOffline,
                isOnlineAvailable: DatabaseRuntimeState.IsOnlineAvailable,
                isOfflineDatabaseAvailable: DatabaseRuntimeState.IsOfflineDatabaseAvailable,
                statusMessage: DatabaseRuntimeState.StatusMessage);

            UpdateToggleLabels();
            UpdateDatabaseTarget();
            await InitializeDatabaseAsync();
        }

        private void SyncToggleToCurrentState()
        {
            DatabaseModeToggle.Checked -= DatabaseModeToggle_Changed;
            DatabaseModeToggle.Unchecked -= DatabaseModeToggle_Changed;
            DatabaseModeToggle.IsChecked = DatabaseRuntimeState.UseOfflineDatabase;
            DatabaseModeToggle.Checked += DatabaseModeToggle_Changed;
            DatabaseModeToggle.Unchecked += DatabaseModeToggle_Changed;

            UpdateToggleLabels();
            UpdateToggleAvailability();
        }

        private void UpdateToggleLabels()
        {
            if (DatabaseModeToggle.IsChecked == true)
            {
                DatabaseModeLabel.Text = "Offline Database";
                DatabaseModeDescription.Text = "Using local MySQL mirror";
            }
            else
            {
                DatabaseModeLabel.Text = "Online Database";
                DatabaseModeDescription.Text = "Using Hostinger MySQL (remote)";
            }
        }

        private void UpdateToggleAvailability()
        {
            // Disable the toggle if offline DB is not configured at all
            DatabaseModeToggle.IsEnabled = DatabaseHelper.IsOfflineConfigured();
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

        private async Task InitializeDatabaseAsync(bool showSuccessMessage = false)
        {
            if (_isInitializingDatabase)
            {
                return;
            }

            _isInitializingDatabase = true;
            UpdateInteractiveState();
            UpdateDatabaseTarget();
            SetStatus(string.Empty);
            SetDatabaseStatus("Connecting to the database...", isError: false);

            try
            {
                var initializationState = await Task.Run(() => LoadInitializationState());
                var result = initializationState.Result;

                SetDatabaseReady(result.IsReady);
                SetDatabaseStatus(result.Message, isError: !result.IsReady);
                UpdateDatabaseTarget();
                BrandingVisualHelper.ApplyLogo(BrandLogoImage, BrandLogoFallbackPanel, initializationState.LogoImage);

                if (showSuccessMessage && result.IsReady)
                {
                    MessageBox.Show(
                        "Database configuration saved successfully.",
                        "Database Settings",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                SyncToggleToCurrentState();
                UsernameTextBox.Focus();
            }
            catch (Exception ex)
            {
                SetDatabaseReady(false);
                SetDatabaseStatus(
                    $"Cannot connect to the database.\nOpen Database Settings and update the server details.\n\n{ex.Message}",
                    isError: true);
                BrandingVisualHelper.ApplyLogo(BrandLogoImage, BrandLogoFallbackPanel, null);
                SyncToggleToCurrentState();
            }
            finally
            {
                _isInitializingDatabase = false;
                UpdateInteractiveState();
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
            var modeLabel = DatabaseRuntimeState.UseOfflineDatabase ? "Local MySQL mirror" : "Online MySQL";
            DatabaseTargetTextBlock.Text = $"{DatabaseHelper.GetActiveConnectionSummary()} ({modeLabel}, {sourceLabel})";
        }

        private void SetDatabaseReady(bool isReady)
        {
            _isDatabaseReady = isReady;
            UpdateInteractiveState();
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

        private void UpdateInteractiveState()
        {
            var isBusy = _isInitializingDatabase || _isAuthenticating;
            UsernameTextBox.IsEnabled = _isDatabaseReady && !isBusy;
            PasswordBox.IsEnabled = _isDatabaseReady && !isBusy;
            LoginButton.IsEnabled = _isDatabaseReady && !isBusy;
            DatabaseSettingsButton.IsEnabled = !isBusy;
            LoginButton.Content = _isAuthenticating ? "Signing In..." : "Sign In";
            DatabaseSettingsButton.Content = _isInitializingDatabase ? "Checking Database..." : "Database Settings";
        }

        private InitializationState LoadInitializationState()
        {
            var result = MySqlOfflineSyncService.InitializeRuntime();
            byte[]? logoImage = null;

            try
            {
                logoImage = _appBrandingRepository.GetBranding().LogoImage;
            }
            catch
            {
                // Leave the fallback logo visible if branding cannot be loaded.
            }

            return new InitializationState(result, logoImage);
        }

        private sealed record InitializationState(OfflineSyncInitializationResult Result, byte[]? LogoImage);
    }
}
