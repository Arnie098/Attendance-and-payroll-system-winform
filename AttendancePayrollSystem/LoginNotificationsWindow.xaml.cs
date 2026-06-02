using System.Windows;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem
{
    public partial class LoginNotificationsWindow : Window
    {
        public LoginNotificationsWindow(LoginNotificationSnapshot snapshot)
        {
            InitializeComponent();
            DataContext = snapshot;
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
