using System.Windows;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            DotEnv.Load();
            var loginWindow = new LoginWindow();
            MainWindow = loginWindow;
            loginWindow.Show();
        }
    }
}
