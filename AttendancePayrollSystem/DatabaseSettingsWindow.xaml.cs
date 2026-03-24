using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem
{
    public partial class DatabaseSettingsWindow : Window
    {
        public DatabaseSettingsWindow()
        {
            InitializeComponent();
            SslModeComboBox.ItemsSource = DatabaseConnectionSettingsStore.GetSupportedSslModes();
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            var settings = DatabaseConnectionSettingsStore.LoadCurrent();
            ServerTextBox.Text = settings.Server;
            PortTextBox.Text = settings.Port.ToString(CultureInfo.InvariantCulture);
            DatabaseTextBox.Text = settings.Database;
            UsernameTextBox.Text = settings.Username;
            PasswordBox.Password = settings.Password;
            SslModeComboBox.SelectedItem = settings.SslMode;

            if (SslModeComboBox.SelectedItem == null)
            {
                SslModeComboBox.SelectedIndex = 0;
            }
        }

        private void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = ReadSettingsFromForm();
                var connectionString = DatabaseConnectionSettingsStore.BuildConnectionString(settings);
                DatabaseHelper.VerifyConnection(connectionString);
                SetStatus($"Connection successful. Target: {DatabaseHelper.GetConnectionSummary(connectionString)}", isError: false);
            }
            catch (Exception ex)
            {
                SetStatus($"Connection failed.\n{ex.Message}", isError: true);
            }
        }

        private void UseDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Remove the saved laptop-specific database settings and use the app default connection again?",
                "Use App Default",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            DatabaseConnectionSettingsStore.ClearOverride();
            DialogResult = true;
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DatabaseConnectionSettingsStore.Save(ReadSettingsFromForm());
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, isError: true);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private DatabaseConnectionSettings ReadSettingsFromForm()
        {
            if (string.IsNullOrWhiteSpace(ServerTextBox.Text))
            {
                throw new InvalidOperationException("Database host or IP address is required.");
            }

            if (!uint.TryParse(PortTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port == 0)
            {
                throw new InvalidOperationException("Port must be a valid positive number.");
            }

            if (string.IsNullOrWhiteSpace(DatabaseTextBox.Text))
            {
                throw new InvalidOperationException("Database name is required.");
            }

            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                throw new InvalidOperationException("Database username is required.");
            }

            return new DatabaseConnectionSettings
            {
                Server = ServerTextBox.Text.Trim(),
                Port = port,
                Database = DatabaseTextBox.Text.Trim(),
                Username = UsernameTextBox.Text.Trim(),
                Password = PasswordBox.Password,
                SslMode = Convert.ToString(SslModeComboBox.SelectedItem) ?? "Preferred"
            };
        }

        private void SetStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(185, 28, 28))
                : new SolidColorBrush(Color.FromRgb(21, 128, 61));
            StatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
