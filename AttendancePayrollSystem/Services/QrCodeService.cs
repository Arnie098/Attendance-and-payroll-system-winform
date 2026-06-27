using System;
using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace AttendancePayrollSystem.Services;

public class QrCodeService
{
    private const int PixelsPerModule = 10;

    public byte[] GeneratePng(string employeeCode, int pixelsPerModule = PixelsPerModule)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            throw new ArgumentException("Employee code must not be blank.", nameof(employeeCode));

        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(employeeCode, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(data);
        return qrCode.GetGraphic(pixelsPerModule);
    }

    public BitmapSource Generate(string employeeCode, int pixelsPerModule = PixelsPerModule)
    {
        byte[] png = GeneratePng(employeeCode, pixelsPerModule);
        using var ms = new MemoryStream(png);
        var decoder = new PngBitmapDecoder(ms,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }
}
