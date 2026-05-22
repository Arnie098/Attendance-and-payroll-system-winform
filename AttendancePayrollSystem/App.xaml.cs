using System.Windows;
using AttendancePayrollSystem.Services;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AttendancePayrollSystem
{
    public partial class App : Application
    {
        private DispatcherTimer? _offlineSyncTimer;
        private bool _offlineSyncInProgress;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            DotEnv.Load();
            AppLogger.Info("Application starting up.");
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

        private async void OfflineSyncTimer_Tick(object? sender, EventArgs e)
        {
            if (_offlineSyncInProgress)
            {
                return;
            }

            _offlineSyncInProgress = true;
            try
            {
                await Task.Run(() => MySqlOfflineSyncService.TrySynchronizeNow());
            }
            catch
            {
                // Keep the timer alive; runtime state captures sync failures for the UI.
                AppLogger.Warn("Background offline sync tick failed silently.");
            }
            finally
            {
                _offlineSyncInProgress = false;
            }
        }
    }
}
