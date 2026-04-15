using System.Windows;
using AttendancePayrollSystem.DataAccess;

namespace AttendancePayrollSystem
{
    public partial class DatabaseBackupModeWindow : Window
    {
        public DatabaseBackupModeWindow()
        {
            InitializeComponent();
        }

        public DatabaseBackupMode SelectedMode =>
            FullBackupRadioButton.IsChecked == true
                ? DatabaseBackupMode.Full
                : DifferentialBackupRadioButton.IsChecked == true
                    ? DatabaseBackupMode.Differential
                    : DatabaseBackupMode.Incremental;

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
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
