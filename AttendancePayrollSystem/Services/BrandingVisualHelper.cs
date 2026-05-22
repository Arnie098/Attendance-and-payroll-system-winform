using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AttendancePayrollSystem.Services
{
    public static class BrandingVisualHelper
    {
        public static void ApplyLogo(Image imageControl, UIElement fallbackElement, byte[]? logoImage)
        {
            imageControl.Source = CreateImageSource(logoImage);
            var hasLogo = imageControl.Source != null;
            imageControl.Visibility = hasLogo ? Visibility.Visible : Visibility.Collapsed;
            fallbackElement.Visibility = hasLogo ? Visibility.Collapsed : Visibility.Visible;
        }

        private static BitmapImage? CreateImageSource(byte[]? imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                return null;
            }

            var image = new BitmapImage();
            using var memoryStream = new MemoryStream(imageBytes);
            image.BeginInit();
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = memoryStream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
