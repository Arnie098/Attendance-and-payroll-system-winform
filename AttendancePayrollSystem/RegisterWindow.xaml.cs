using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem
{
    public partial class RegisterWindow : Window
    {
        private readonly AuthRepository _authRepository = new();
        private byte[]? _selectedProfileImage;

        public Employee? RegisteredEmployee { get; private set; }
        public string RegisteredUsername { get; private set; } = string.Empty;
        public EmployeeRegistrationRequest? ResultRequest { get; private set; }

        public RegisterWindow()
        {
            InitializeComponent();
            HireDatePicker.SelectedDate = DateTime.Today;
            RefreshPhotoPreview();
        }

        private void ChoosePhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ProfileImageFilePicker.TryPick(this, out var imageBytes))
                {
                    return;
                }

                _selectedProfileImage = imageBytes;
                RefreshPhotoPreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load profile photo.\n{ex.Message}",
                    "Image Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RemovePhoto_Click(object sender, RoutedEventArgs e)
        {
            _selectedProfileImage = null;
            RefreshPhotoPreview();
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            SetStatus(string.Empty);

            if (string.IsNullOrWhiteSpace(FullNameTextBox.Text) ||
                string.IsNullOrWhiteSpace(UsernameTextBox.Text) ||
                string.IsNullOrWhiteSpace(PasswordBox.Password))
            {
                SetStatus("Full name, username, and password are required.");
                return;
            }

            if (!string.Equals(PasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
            {
                SetStatus("Password confirmation does not match.");
                return;
            }

            if (HireDatePicker.SelectedDate == null)
            {
                SetStatus("Please choose a hire date.");
                return;
            }

            var request = new EmployeeRegistrationRequest
            {
                FullName = FullNameTextBox.Text.Trim(),
                Username = UsernameTextBox.Text.Trim(),
                Password = PasswordBox.Password,
                Email = EmailTextBox.Text.Trim(),
                Phone = PhoneTextBox.Text.Trim(),
                Position = PositionTextBox.Text.Trim(),
                Department = DepartmentTextBox.Text.Trim(),
                HireDate = HireDatePicker.SelectedDate.Value,
                ProfileImage = _selectedProfileImage
            };

            try
            {
                var employee = _authRepository.RegisterEmployee(request);
                ResultRequest = request;
                RegisteredEmployee = employee;
                RegisteredUsername = request.Username.Trim().ToLowerInvariant();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SetStatus(string message)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void RefreshPhotoPreview()
        {
            PhotoPreviewImage.Source = CreateImage(_selectedProfileImage);
            PhotoPlaceholderText.Visibility = _selectedProfileImage == null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static BitmapImage? CreateImage(byte[]? imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                return null;
            }

            var image = new BitmapImage();
            using var stream = new MemoryStream(imageBytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
