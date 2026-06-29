using System;
using System.Windows;
using AttendancePayrollSystem.DataAccess;

namespace AttendancePayrollSystem
{
    public partial class PayslipSettingsWindow : Window
    {
        private readonly AppBrandingRepository _brandingRepository = new();

        public PayslipSettingsWindow()
        {
            InitializeComponent();
            LoadCurrentValues();
        }

        private void LoadCurrentValues()
        {
            try
            {
                var branding = _brandingRepository.GetBranding();
                OfficerNameTextBox.Text = branding.CertifyingOfficerName;
                OfficerTitleTextBox.Text = branding.CertifyingOfficerTitle;
            }
            catch
            {
                // Leave fields empty if loading fails
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _brandingRepository.UpdateCertifyingOfficer(
                    OfficerNameTextBox.Text.Trim(),
                    OfficerTitleTextBox.Text.Trim());
                MessageBox.Show("Payslip settings saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
