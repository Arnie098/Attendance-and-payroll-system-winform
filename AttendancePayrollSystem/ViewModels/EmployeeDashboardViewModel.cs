using System.Collections.ObjectModel;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.ViewModels
{
    public class EmployeeDashboardViewModel : BaseViewModel
    {
        private string _welcomeText = string.Empty;
        private string _todayText = string.Empty;
        private string _employeeCodeText = string.Empty;
        private string _positionText = string.Empty;
        private string _departmentText = string.Empty;
        private string _hourlyRateText = string.Empty;
        private string _attendanceStatusText = string.Empty;
        private string _timeInText = "-";
        private string _timeOutText = "-";
        private byte[]? _profileImage;
        private bool _hasProfileImage;
        private string _clockActionButtonText = "Open Attendance";
        private bool _isClockActionEnabled = true;
        private string _latestPayrollText = "No payroll records yet.";

        public ObservableCollection<Attendance> AttendanceHistory { get; } = new();
        public ObservableCollection<Payroll> PayrollHistory { get; } = new();

        public string WelcomeText
        {
            get => _welcomeText;
            set => SetProperty(ref _welcomeText, value);
        }

        public string TodayText
        {
            get => _todayText;
            set => SetProperty(ref _todayText, value);
        }

        public string EmployeeCodeText
        {
            get => _employeeCodeText;
            set => SetProperty(ref _employeeCodeText, value);
        }

        public string PositionText
        {
            get => _positionText;
            set => SetProperty(ref _positionText, value);
        }

        public string DepartmentText
        {
            get => _departmentText;
            set => SetProperty(ref _departmentText, value);
        }

        public string HourlyRateText
        {
            get => _hourlyRateText;
            set => SetProperty(ref _hourlyRateText, value);
        }

        public string AttendanceStatusText
        {
            get => _attendanceStatusText;
            set => SetProperty(ref _attendanceStatusText, value);
        }

        public string TimeInText
        {
            get => _timeInText;
            set => SetProperty(ref _timeInText, value);
        }

        public string TimeOutText
        {
            get => _timeOutText;
            set => SetProperty(ref _timeOutText, value);
        }

        public byte[]? ProfileImage
        {
            get => _profileImage;
            set
            {
                if (SetProperty(ref _profileImage, value))
                {
                    HasProfileImage = value != null && value.Length > 0;
                }
            }
        }

        public bool HasProfileImage
        {
            get => _hasProfileImage;
            set => SetProperty(ref _hasProfileImage, value);
        }

        public string ClockActionButtonText
        {
            get => _clockActionButtonText;
            set => SetProperty(ref _clockActionButtonText, value);
        }

        public bool IsClockActionEnabled
        {
            get => _isClockActionEnabled;
            set => SetProperty(ref _isClockActionEnabled, value);
        }

        public string LatestPayrollText
        {
            get => _latestPayrollText;
            set => SetProperty(ref _latestPayrollText, value);
        }
    }
}
