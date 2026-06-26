using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.ViewModels
{
    public class EmployeeDashboardViewModel : BaseViewModel
    {
        private const int AttendancePageSize = 10;
        private const int PayrollPageSize = 10;

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
        private bool _hasBiometricTemplate;
        private string _biometricStatusText = "Fingerprint not enrolled.";
        private string _clockActionButtonText = "Open Attendance";
        private bool _isClockActionEnabled = true;
        private string _latestPayrollText = "No payroll records yet.";
        private string _latestLeaveText = "No leave requests yet.";
        private bool _canCancelSelectedLeave;

        // Full backing lists for pagination (summaries derive from these)
        private List<Attendance> _allAttendance = new();
        private List<Payroll> _allPayroll = new();

        // Attendance pagination state
        private int _attendancePage = 1;
        private string _attendancePageStatus = string.Empty;
        private bool _canAttendancePrevious;
        private bool _canAttendanceNext;

        // Payroll pagination state
        private int _payrollPage = 1;
        private string _payrollPageStatus = string.Empty;
        private bool _canPayrollPrevious;
        private bool _canPayrollNext;

        /// <summary>Paged attendance rows shown in the grid.</summary>
        public ObservableCollection<Attendance> AttendanceHistory { get; } = new();

        /// <summary>Paged payroll rows shown in the grid.</summary>
        public ObservableCollection<Payroll> PayrollHistory { get; } = new();

        public ObservableCollection<LeaveRequest> LeaveRequests { get; } = new();

        // ── Attendance pagination properties ──

        public string AttendancePageStatus
        {
            get => _attendancePageStatus;
            private set => SetProperty(ref _attendancePageStatus, value);
        }

        public bool CanAttendancePrevious
        {
            get => _canAttendancePrevious;
            private set => SetProperty(ref _canAttendancePrevious, value);
        }

        public bool CanAttendanceNext
        {
            get => _canAttendanceNext;
            private set => SetProperty(ref _canAttendanceNext, value);
        }

        // ── Payroll pagination properties ──

        public string PayrollPageStatus
        {
            get => _payrollPageStatus;
            private set => SetProperty(ref _payrollPageStatus, value);
        }

        public bool CanPayrollPrevious
        {
            get => _canPayrollPrevious;
            private set => SetProperty(ref _canPayrollPrevious, value);
        }

        public bool CanPayrollNext
        {
            get => _canPayrollNext;
            private set => SetProperty(ref _canPayrollNext, value);
        }

        // ── Pagination commands ──

        public void AttendancePreviousPage()
        {
            if (_attendancePage > 1)
            {
                _attendancePage--;
                RefreshAttendancePage();
            }
        }

        public void AttendanceNextPage()
        {
            if (_attendancePage < AttendanceTotalPages)
            {
                _attendancePage++;
                RefreshAttendancePage();
            }
        }

        public void PayrollPreviousPage()
        {
            if (_payrollPage > 1)
            {
                _payrollPage--;
                RefreshPayrollPage();
            }
        }

        public void PayrollNextPage()
        {
            if (_payrollPage < PayrollTotalPages)
            {
                _payrollPage++;
                RefreshPayrollPage();
            }
        }

        // ── Data setters (called from code-behind) ──

        /// <summary>
        /// Replaces the full attendance collection and resets to page 1.
        /// </summary>
        public void SetAttendanceData(IEnumerable<Attendance> records)
        {
            _allAttendance = records?.ToList() ?? new List<Attendance>();
            _attendancePage = 1;
            RefreshAttendancePage();
        }

        /// <summary>
        /// Replaces the full payroll collection, updates the "Latest Payroll" summary,
        /// and resets to page 1.
        /// </summary>
        public void SetPayrollData(IReadOnlyList<Payroll> records)
        {
            _allPayroll = records?.ToList() ?? new List<Payroll>();
            _payrollPage = 1;
            RefreshPayrollPage();

            // Summary card always reflects the full data set
            var latest = _allPayroll.FirstOrDefault();
            LatestPayrollText = latest == null
                ? "No payroll records yet."
                : $"{latest.PayPeriodStart:yyyy-MM-dd} to {latest.PayPeriodEnd:yyyy-MM-dd} | Net Pay: PHP {latest.NetPay:N2} ({latest.Status})";
        }

        // ── Internal helpers ──

        private int AttendanceTotalPages => Math.Max(1, (int)Math.Ceiling((double)_allAttendance.Count / AttendancePageSize));
        private int PayrollTotalPages => Math.Max(1, (int)Math.Ceiling((double)_allPayroll.Count / PayrollPageSize));

        private void RefreshAttendancePage()
        {
            var page = _allAttendance
                .Skip((_attendancePage - 1) * AttendancePageSize)
                .Take(AttendancePageSize);

            AttendanceHistory.Clear();
            foreach (var item in page)
            {
                AttendanceHistory.Add(item);
            }

            var total = AttendanceTotalPages;
            AttendancePageStatus = _allAttendance.Count == 0
                ? "No records"
                : $"Page {_attendancePage} of {total}  ({_allAttendance.Count} records)";
            CanAttendancePrevious = _attendancePage > 1;
            CanAttendanceNext = _attendancePage < total;
        }

        private void RefreshPayrollPage()
        {
            var page = _allPayroll
                .Skip((_payrollPage - 1) * PayrollPageSize)
                .Take(PayrollPageSize);

            PayrollHistory.Clear();
            foreach (var item in page)
            {
                PayrollHistory.Add(item);
            }

            var total = PayrollTotalPages;
            PayrollPageStatus = _allPayroll.Count == 0
                ? "No records"
                : $"Page {_payrollPage} of {total}  ({_allPayroll.Count} records)";
            CanPayrollPrevious = _payrollPage > 1;
            CanPayrollNext = _payrollPage < total;
        }

        // ── Existing properties ──

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

        public bool HasBiometricTemplate
        {
            get => _hasBiometricTemplate;
            set => SetProperty(ref _hasBiometricTemplate, value);
        }

        public string BiometricStatusText
        {
            get => _biometricStatusText;
            set => SetProperty(ref _biometricStatusText, value);
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

        public string LatestLeaveText
        {
            get => _latestLeaveText;
            set => SetProperty(ref _latestLeaveText, value);
        }

        public bool CanCancelSelectedLeave
        {
            get => _canCancelSelectedLeave;
            set => SetProperty(ref _canCancelSelectedLeave, value);
        }
    }
}
