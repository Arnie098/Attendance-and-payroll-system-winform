using System.Windows;
using AttendancePayrollSystem.Services;
using System.Threading.Tasks;
using System.Windows.Threading;
using System;

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

            if (TryHandleStartupCommand(e.Args))
            {
                return;
            }

            StartOfflineSyncTimerIfConfigured();
            var loginWindow = new LoginWindow();
            MainWindow = loginWindow;
            loginWindow.Show();
        }

        private bool TryHandleStartupCommand(string[] args)
        {
            if (args.Length == 0)
            {
                return false;
            }

            if (!Array.Exists(args, arg => string.Equals(arg, "--seed-attendance-payroll", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            try
            {
                var result = new SampleDataSeeder().SeedAttendanceAndPayroll();
                MessageBox.Show(
                    result.Message,
                    "Seed Attendance And Payroll",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                AppLogger.Info($"Attendance/payroll seed completed. {result.Message}");
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Attendance/payroll seed failed");
                MessageBox.Show(
                    $"Failed to seed attendance and payroll data.\n{ex.Message}",
                    "Seed Attendance And Payroll",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            Shutdown();
            return true;
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
