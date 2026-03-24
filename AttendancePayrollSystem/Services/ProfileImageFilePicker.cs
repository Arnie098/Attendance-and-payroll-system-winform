using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AttendancePayrollSystem.Services
{
    public static class ProfileImageFilePicker
    {
        private const long MaxProfileImageBytes = 5 * 1024 * 1024;

        public static bool TryPick(Window owner, out byte[]? imageBytes)
        {
            imageBytes = null;

            var dialog = new OpenFileDialog
            {
                Title = "Choose Profile Photo",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(owner) != true)
            {
                return false;
            }

            var file = new FileInfo(dialog.FileName);
            if (file.Length > MaxProfileImageBytes)
            {
                throw new InvalidOperationException("Profile photo must be 5 MB or smaller.");
            }

            imageBytes = File.ReadAllBytes(dialog.FileName);
            return true;
        }
    }
}
