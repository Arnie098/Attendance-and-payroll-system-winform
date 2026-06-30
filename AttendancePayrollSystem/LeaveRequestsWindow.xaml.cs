using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem
{
    public partial class LeaveRequestsWindow : Window
    {
        private readonly LeaveRequestRepository _leaveRequestRepository = new();
        private readonly LeaveDocumentRepository _leaveDocumentRepository = new();
        private readonly string _currentUsername;
        private LeaveRequest? _selectedLeaveRequest;

        public LeaveRequestsWindow(string currentUsername)
        {
            InitializeComponent();
            _currentUsername = string.IsNullOrWhiteSpace(currentUsername) ? "admin" : currentUsername.Trim();
            Loaded += LeaveRequestsWindow_Loaded;
        }

        private async void LeaveRequestsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= LeaveRequestsWindow_Loaded;
            await LoadLeaveRequestsAsync();
        }

        private async Task LoadLeaveRequestsAsync()
        {
            var selectedLeaveRequestId = _selectedLeaveRequest?.LeaveRequestId;
            var requests = await Task.Run(() => _leaveRequestRepository.GetLeaveRequests());
            ApplyLeaveRequests(requests, selectedLeaveRequestId);
        }

        private void ApplyLeaveRequests(IReadOnlyList<LeaveRequest> requests, int? selectedLeaveRequestId)
        {
            LeaveRequestsDataGrid.ItemsSource = requests;

            if (!selectedLeaveRequestId.HasValue)
            {
                ResetLeaveRequestReviewPanel();
                return;
            }

            var refreshedSelection = requests.FirstOrDefault(request => request.LeaveRequestId == selectedLeaveRequestId.Value);
            if (refreshedSelection == null)
            {
                ResetLeaveRequestReviewPanel();
                return;
            }

            LeaveRequestsDataGrid.SelectedItem = refreshedSelection;
            LeaveRequestsDataGrid.ScrollIntoView(refreshedSelection);
        }

        private async void RefreshLeaveRequests_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await LoadLeaveRequestsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load leave requests.\n{ex.Message}",
                    "Leave Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void LeaveRequestsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshLeaveRequestReviewPanel(LeaveRequestsDataGrid.SelectedItem as LeaveRequest);
        }

        private async void ApproveLeaveRequest_Click(object sender, RoutedEventArgs e)
        {
            await ReviewSelectedLeaveRequestAsync(approve: true);
        }

        private async void RejectLeaveRequest_Click(object sender, RoutedEventArgs e)
        {
            await ReviewSelectedLeaveRequestAsync(approve: false);
        }

        private async Task ReviewSelectedLeaveRequestAsync(bool approve)
        {
            if (_selectedLeaveRequest == null)
            {
                MessageBox.Show(
                    "Select a leave request first.",
                    "Leave Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!string.Equals(_selectedLeaveRequest.Status, LeavePolicies.StatusPending, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Only pending leave requests can be reviewed.",
                    "Leave Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var actionText = approve ? "approve" : "reject";
            var successText = approve ? "approved" : "rejected";
            var result = MessageBox.Show(
                $"Are you sure you want to {actionText} leave request #{_selectedLeaveRequest.LeaveRequestId} for {_selectedLeaveRequest.EmployeeName}?",
                $"Confirm {char.ToUpper(actionText[0])}{actionText[1..]}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var reviewerNotes = LeaveReviewNotesTextBox.Text.Trim();

            // For approvals, check if existing attendance records will be overridden.
            bool overrideAttendance = false;
            if (approve)
            {
                List<DateTime> conflicts;
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    var empId = _selectedLeaveRequest.EmployeeId;
                    var start  = _selectedLeaveRequest.StartDate;
                    var end    = _selectedLeaveRequest.EndDate;
                    conflicts = await Task.Run(() => _leaveRequestRepository.GetAttendanceConflictDates(empId, start, end));
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }

                if (conflicts.Count > 0)
                {
                    var dateList = string.Join("\n  • ", conflicts.Select(d => d.ToString("yyyy-MM-dd")));
                    var overrideResult = MessageBox.Show(
                        $"The following date(s) already have attendance records with time punches:\n\n  • {dateList}\n\n" +
                        $"Approving will wipe those time records and replace them with leave status.\n\nProceed anyway?",
                        "Attendance Conflict — Override?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (overrideResult != MessageBoxResult.Yes)
                        return;

                    overrideAttendance = true;
                }
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var leaveRequestId = _selectedLeaveRequest.LeaveRequestId;
                await Task.Run(() =>
                {
                    if (approve)
                    {
                        _leaveRequestRepository.ApproveLeaveRequest(leaveRequestId, _currentUsername, reviewerNotes, overrideAttendance);
                    }
                    else
                    {
                        _leaveRequestRepository.RejectLeaveRequest(leaveRequestId, _currentUsername, reviewerNotes);
                    }
                });

                await LoadLeaveRequestsAsync();
                MessageBox.Show(
                    $"Leave request {successText} successfully.",
                    "Leave Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to {actionText} leave request.\n{ex.Message}",
                    "Leave Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void RefreshLeaveRequestReviewPanel(LeaveRequest? leaveRequest)
        {
            _selectedLeaveRequest = leaveRequest;
            if (leaveRequest == null)
            {
                ResetLeaveRequestReviewPanel();
                return;
            }

            SelectedLeaveEmployeeTextBlock.Text = $"{leaveRequest.EmployeeCode} - {leaveRequest.EmployeeName}";
            SelectedLeaveTypeTextBlock.Text = leaveRequest.LeaveType;
            SelectedLeaveDatesTextBlock.Text = $"{leaveRequest.StartDate:yyyy-MM-dd} to {leaveRequest.EndDate:yyyy-MM-dd}";
            SelectedLeaveStatusTextBlock.Text = leaveRequest.Status;
            SelectedLeavePaymentTextBlock.Text = $"{leaveRequest.PaymentLabel} ({LeavePolicies.GetAttendanceStatus(leaveRequest.IsPaid)})";
            SelectedLeaveChargeableDaysTextBlock.Text = leaveRequest.ChargeableDays.ToString();
            SelectedLeaveReviewedByTextBlock.Text = string.IsNullOrWhiteSpace(leaveRequest.ReviewedBy)
                ? "Pending review"
                : leaveRequest.ReviewedAt.HasValue
                    ? $"{leaveRequest.ReviewedBy} on {leaveRequest.ReviewedAt.Value:yyyy-MM-dd HH:mm}"
                    : leaveRequest.ReviewedBy;
            SelectedLeaveReasonTextBlock.Text = leaveRequest.Reason;
            SelectedLeaveReviewHistoryTextBlock.Text = BuildLeaveReviewHistoryText(leaveRequest);

            try
            {
                var docs = _leaveDocumentRepository.GetByLeaveRequestId(leaveRequest.LeaveRequestId);
                ReviewDocumentsListBox.ItemsSource = docs;
                if (docs.Count > 0)
                {
                    ReviewDocumentsListBox.Visibility = Visibility.Visible;
                    NoDocumentsTextBlock.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ReviewDocumentsListBox.Visibility = Visibility.Collapsed;
                    NoDocumentsTextBlock.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                ReviewDocumentsListBox.ItemsSource = null;
                ReviewDocumentsListBox.Visibility = Visibility.Collapsed;
                NoDocumentsTextBlock.Visibility = Visibility.Visible;
            }

            var canReview = string.Equals(leaveRequest.Status, LeavePolicies.StatusPending, StringComparison.OrdinalIgnoreCase);
            ApproveLeaveRequestButton.IsEnabled = canReview;
            RejectLeaveRequestButton.IsEnabled = canReview;
            LeaveReviewNotesTextBox.IsEnabled = canReview;
            LeaveReviewNotesTextBox.Text = canReview ? string.Empty : leaveRequest.ReviewerNotes;
        }

        private void OpenDocument_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: Models.LeaveDocument doc })
                return;

            var ext = Path.GetExtension(doc.DocumentName);
            var tempPath = Path.Combine(Path.GetTempPath(), $"leave_doc_{doc.DocumentId}{ext}");

            try
            {
                File.WriteAllBytes(tempPath, doc.DocumentData);
                Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open the document.\n{ex.Message}",
                    "Document", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetLeaveRequestReviewPanel()
        {
            _selectedLeaveRequest = null;
            SelectedLeaveEmployeeTextBlock.Text = "No leave request selected.";
            SelectedLeaveTypeTextBlock.Text = "-";
            SelectedLeaveDatesTextBlock.Text = "-";
            SelectedLeaveStatusTextBlock.Text = "-";
            SelectedLeavePaymentTextBlock.Text = "-";
            SelectedLeaveChargeableDaysTextBlock.Text = "-";
            SelectedLeaveReviewedByTextBlock.Text = "-";
            SelectedLeaveReasonTextBlock.Text = "Select a leave request from the list to review its details.";
            SelectedLeaveReviewHistoryTextBlock.Text = "No review activity yet.";
            ReviewDocumentsListBox.ItemsSource = null;
            ReviewDocumentsListBox.Visibility = Visibility.Collapsed;
            NoDocumentsTextBlock.Visibility = Visibility.Visible;
            LeaveReviewNotesTextBox.IsEnabled = false;
            LeaveReviewNotesTextBox.Text = string.Empty;
            ApproveLeaveRequestButton.IsEnabled = false;
            RejectLeaveRequestButton.IsEnabled = false;
        }

        private static string BuildLeaveReviewHistoryText(LeaveRequest leaveRequest)
        {
            if (string.IsNullOrWhiteSpace(leaveRequest.ReviewedBy) && string.IsNullOrWhiteSpace(leaveRequest.ReviewerNotes))
            {
                return "No review activity yet.";
            }

            if (string.IsNullOrWhiteSpace(leaveRequest.ReviewerNotes))
            {
                return string.IsNullOrWhiteSpace(leaveRequest.ReviewedBy)
                    ? "No reviewer notes recorded."
                    : leaveRequest.ReviewedAt.HasValue
                        ? $"{leaveRequest.ReviewedBy} reviewed this request on {leaveRequest.ReviewedAt.Value:yyyy-MM-dd HH:mm}."
                        : $"{leaveRequest.ReviewedBy} reviewed this request.";
            }

            if (string.IsNullOrWhiteSpace(leaveRequest.ReviewedBy))
            {
                return leaveRequest.ReviewerNotes;
            }

            return leaveRequest.ReviewedAt.HasValue
                ? $"{leaveRequest.ReviewedBy} reviewed this request on {leaveRequest.ReviewedAt.Value:yyyy-MM-dd HH:mm}.\n{leaveRequest.ReviewerNotes}"
                : $"{leaveRequest.ReviewedBy} reviewed this request.\n{leaveRequest.ReviewerNotes}";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
