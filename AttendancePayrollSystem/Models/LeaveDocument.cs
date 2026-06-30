using System;

namespace AttendancePayrollSystem.Models
{
    public class LeaveDocument
    {
        public int DocumentId { get; set; }
        public int LeaveRequestId { get; set; }
        public string DocumentName { get; set; } = string.Empty;
        public byte[] DocumentData { get; set; } = Array.Empty<byte>();
        public long FileSizeBytes { get; set; }
        public DateTime UploadedAt { get; set; }

        public string FormattedSize =>
            FileSizeBytes >= 1024 * 1024
                ? $"{FileSizeBytes / (1024.0 * 1024.0):N1} MB"
                : $"{FileSizeBytes / 1024.0:N1} KB";
    }
}
