using System;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using Microsoft.Win32;

namespace AttendancePayrollSystem;

public partial class QrCodeModal : Window
{
    private readonly Employee _employee;
    private readonly QrCodeService _qrCodeService = new();
    private byte[]? _pngBytes;

    public QrCodeModal(Employee employee)
    {
        InitializeComponent();
        _employee = employee;
        EmployeeLabel.Text = $"{employee.EmployeeCode} · {employee.FullName}";
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        LoadQrCode();
    }

    private void LoadQrCode()
    {
        try
        {
            _pngBytes = _qrCodeService.GeneratePng(_employee.EmployeeCode);
            using var ms = new MemoryStream(_pngBytes);
            var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(ms,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            QrImage.Source = decoder.Frames[0];
        }
        catch (Exception ex)
        {
            QrImage.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = false;
            PrintButton.IsEnabled = false;
            ErrorLabel.Text = $"Could not generate QR code: {ex.Message}";
            ErrorLabel.Visibility = Visibility.Visible;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_pngBytes is null) return;

        var dlg = new SaveFileDialog
        {
            FileName = $"QR_{_employee.EmployeeCode}.png",
            Filter = "PNG Image|*.png",
            DefaultExt = ".png"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllBytes(dlg.FileName, _pngBytes);
            MessageBox.Show("QR code saved successfully.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;

        try
        {
            dlg.PrintVisual(QrImage, $"QR Code - {_employee.EmployeeCode}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to print: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
