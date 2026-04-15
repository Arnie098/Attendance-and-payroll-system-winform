using System.Windows;
using AttendancePayrollSystem.Services;
using System.Windows.Threading;

namespace AttendancePayrollSystem
{
    public partial class App : Application
    {
        private DispatcherTimer? _offlineSyncTimer;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            DotEnv.Load();
            StartOfflineSyncTimerIfConfigured();
            var loginWindow = new LoginWindow();
            MainWindow = loginWindow;
            loginWindow.Show();
        }

        private void StartOfflineSyncTimerIfConfigured()
        {
            if (!MySqlOfflineSyncService.IsEnabled || !MySqlOfflineSyncService.IsAutoSyncEnabled)
            {
                return;
            }

            _offlineSyncTimer = new DispatcherTimer
            {
                Interval = MySqlOfflineSyncService.SyncInterval
            };
            _offlineSyncTimer.Tick += OfflineSyncTimer_Tick;
            _offlineSyncTimer.Start();
        }

        private void OfflineSyncTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                MySqlOfflineSyncService.TrySynchronizeNow();
            }
            catch
            {
                // Keep the timer alive; runtime state captures sync failures for the UI.
            }
        }
    }
}
