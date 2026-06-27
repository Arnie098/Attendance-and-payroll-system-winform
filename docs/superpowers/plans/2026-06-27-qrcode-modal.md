# QR Code Modal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Show QR Code" button to the Barcode Fallback section of `AttendanceModal` that opens a popup displaying the employee's QR code, with Save PNG and Print options.

**Architecture:** A new `QrCodeService` generates QR images from an employee code using the QRCoder library. A new `QrCodeModal` window displays the QR, employee name, and action buttons. `AttendanceModal` gets one new button that instantiates and shows the modal.

**Tech Stack:** QRCoder 1.6.0 (NuGet), WPF/XAML, MaterialDesignThemes, xUnit (existing test project)

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `AttendancePayrollSystem/AttendancePayrollSystem.csproj` | Add QRCoder package reference |
| Create | `AttendancePayrollSystem/Services/QrCodeService.cs` | Generate PNG bytes and WPF BitmapSource from employee code |
| Create | `AttendancePayrollSystem/QrCodeModal.xaml` | QR popup layout |
| Create | `AttendancePayrollSystem/QrCodeModal.xaml.cs` | QR popup code-behind: load image, save, print |
| Modify | `AttendancePayrollSystem/AttendanceModal.xaml` | Add "Show QR Code" button to Barcode Fallback panel |
| Modify | `AttendancePayrollSystem/AttendanceModal.xaml.cs` | Handle ShowQrCode_Click |
| Create | `AttendancePayrollSystem.Tests/QrCodeServiceTests.cs` | Unit tests for QrCodeService |

---

## Task 1: Add QRCoder NuGet Package

**Files:**
- Modify: `AttendancePayrollSystem/AttendancePayrollSystem.csproj`

- [ ] **Step 1: Add the package reference**

Open `AttendancePayrollSystem/AttendancePayrollSystem.csproj` and add inside the existing `<ItemGroup>` that contains `PackageReference` entries:

```xml
<PackageReference Include="QRCoder" Version="1.6.0" />
```

The `<ItemGroup>` block should look like:

```xml
<ItemGroup>
  <PackageReference Include="MaterialDesignThemes" Version="5.0.0" />
  <PackageReference Include="MaterialDesignColors" Version="3.0.0" />
  <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.8" />
  <PackageReference Include="MySqlConnector" Version="2.5.0" />
  <PackageReference Include="QRCoder" Version="1.6.0" />
  <PackageReference Include="System.Configuration.ConfigurationManager" Version="8.0.0" />
</ItemGroup>
```

- [ ] **Step 2: Restore packages**

```bash
cd AttendancePayrollSystem
dotnet restore
```

Expected output: `Restore complete` with no errors.

- [ ] **Step 3: Commit**

```bash
git add AttendancePayrollSystem/AttendancePayrollSystem.csproj
git commit -m "feat: add QRCoder 1.6.0 package"
```

---

## Task 2: Create QrCodeService

**Files:**
- Create: `AttendancePayrollSystem/Services/QrCodeService.cs`
- Create: `AttendancePayrollSystem.Tests/QrCodeServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AttendancePayrollSystem.Tests/QrCodeServiceTests.cs`:

```csharp
using AttendancePayrollSystem.Services;
using Xunit;

namespace AttendancePayrollSystem.Tests;

public class QrCodeServiceTests
{
    private readonly QrCodeService _sut = new();

    [Fact]
    public void GeneratePng_ReturnsNonEmptyBytes_ForValidCode()
    {
        byte[] result = _sut.GeneratePng("EMP-001");
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void GeneratePng_ReturnsPngMagicBytes_ForValidCode()
    {
        byte[] result = _sut.GeneratePng("T-1001");
        // PNG files start with: 89 50 4E 47 0D 0A 1A 0A
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
        Assert.Equal(0x4E, result[2]);
        Assert.Equal(0x47, result[3]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GeneratePng_ThrowsArgumentException_ForBlankCode(string? code)
    {
        Assert.Throws<ArgumentException>(() => _sut.GeneratePng(code!));
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd AttendancePayrollSystem.Tests
dotnet test --filter "QrCodeServiceTests" -v minimal
```

Expected: build error — `QrCodeService` does not exist yet.

- [ ] **Step 3: Create QrCodeService**

Create `AttendancePayrollSystem/Services/QrCodeService.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd AttendancePayrollSystem.Tests
dotnet test --filter "QrCodeServiceTests" -v minimal
```

Expected:
```
Passed AttendancePayrollSystem.Tests.QrCodeServiceTests.GeneratePng_ReturnsNonEmptyBytes_ForValidCode
Passed AttendancePayrollSystem.Tests.QrCodeServiceTests.GeneratePng_ReturnsPngMagicBytes_ForValidCode
Passed AttendancePayrollSystem.Tests.QrCodeServiceTests.GeneratePng_ThrowsArgumentException_ForBlankCode [null]
Passed AttendancePayrollSystem.Tests.QrCodeServiceTests.GeneratePng_ThrowsArgumentException_ForBlankCode []
Passed AttendancePayrollSystem.Tests.QrCodeServiceTests.GeneratePng_ThrowsArgumentException_ForBlankCode [   ]
```

- [ ] **Step 5: Commit**

```bash
git add AttendancePayrollSystem/Services/QrCodeService.cs AttendancePayrollSystem.Tests/QrCodeServiceTests.cs
git commit -m "feat: add QrCodeService with PNG generation and tests"
```

---

## Task 3: Create QrCodeModal

**Files:**
- Create: `AttendancePayrollSystem/QrCodeModal.xaml`
- Create: `AttendancePayrollSystem/QrCodeModal.xaml.cs`

- [ ] **Step 1: Create QrCodeModal.xaml**

Create `AttendancePayrollSystem/QrCodeModal.xaml`:

```xml
<Window x:Class="AttendancePayrollSystem.QrCodeModal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
        Title="Employee QR Code"
        Width="380" Height="480"
        ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner"
        Background="{StaticResource AppBackgroundBrush}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Dark header -->
        <Border Grid.Row="0"
                Background="#12344D"
                Padding="20,16">
            <StackPanel>
                <TextBlock Text="Employee QR Code"
                           FontSize="18"
                           FontWeight="SemiBold"
                           Foreground="White" />
                <TextBlock x:Name="EmployeeLabel"
                           FontSize="13"
                           Foreground="#9FC2D1"
                           Margin="0,4,0,0" />
            </StackPanel>
        </Border>

        <!-- Content -->
        <StackPanel Grid.Row="1"
                    HorizontalAlignment="Center"
                    VerticalAlignment="Center"
                    Margin="24">

            <Border BorderBrush="#E2E8F0"
                    BorderThickness="1"
                    CornerRadius="8"
                    Padding="12"
                    Background="White"
                    HorizontalAlignment="Center"
                    Margin="0,0,0,16">
                <Image x:Name="QrImage"
                       Width="250"
                       Height="250"
                       RenderOptions.BitmapScalingMode="NearestNeighbor"
                       Stretch="Uniform" />
            </Border>

            <TextBlock x:Name="ErrorLabel"
                       Foreground="#B91C1C"
                       FontSize="12"
                       TextWrapping="Wrap"
                       HorizontalAlignment="Center"
                       Visibility="Collapsed"
                       Margin="0,0,0,12" />

            <StackPanel Orientation="Horizontal"
                        HorizontalAlignment="Center"
                        Margin="0,0,0,10">
                <Button x:Name="SaveButton"
                        Content="Save as PNG"
                        Style="{StaticResource ModernButton}"
                        Background="#0F766E"
                        Margin="0,0,8,0"
                        Click="Save_Click" />
                <Button x:Name="PrintButton"
                        Content="Print"
                        Style="{StaticResource ModernButton}"
                        Background="#1D4ED8"
                        Click="Print_Click" />
            </StackPanel>

            <Button Content="Close"
                    Style="{StaticResource ModernButton}"
                    Background="#475569"
                    HorizontalAlignment="Center"
                    Click="Close_Click" />
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: Create QrCodeModal.xaml.cs**

Create `AttendancePayrollSystem/QrCodeModal.xaml.cs`:

```csharp
using System;
using System.IO;
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
            QrImage.Source = _qrCodeService.Generate(_employee.EmployeeCode);
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
```

- [ ] **Step 3: Build to verify no compile errors**

```bash
cd AttendancePayrollSystem
dotnet build -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add AttendancePayrollSystem/QrCodeModal.xaml AttendancePayrollSystem/QrCodeModal.xaml.cs
git commit -m "feat: add QrCodeModal window with save and print"
```

---

## Task 4: Wire Up "Show QR Code" Button in AttendanceModal

**Files:**
- Modify: `AttendancePayrollSystem/AttendanceModal.xaml` (after line 133, inside the Barcode Fallback `StackPanel`)
- Modify: `AttendancePayrollSystem/AttendanceModal.xaml.cs`

- [ ] **Step 1: Add "Show QR Code" button to AttendanceModal.xaml**

In `AttendancePayrollSystem/AttendanceModal.xaml`, locate the `BarcodeStatusText` TextBlock (around line 128). After the closing `</TextBlock>` tag and before the closing `</StackPanel>` of the Barcode Fallback panel, add:

```xml
                        <Button Margin="0,10,0,0"
                                Style="{StaticResource ModernButton}"
                                Background="#7C3AED"
                                HorizontalAlignment="Left"
                                Click="ShowQrCode_Click"
                                ToolTip="Generate and view this employee's QR code">
                            <StackPanel Orientation="Horizontal">
                                <materialDesign:PackIcon Kind="Qrcode"
                                                         Width="16" Height="16"
                                                         Foreground="White"
                                                         VerticalAlignment="Center"
                                                         Margin="0,0,6,0" />
                                <TextBlock Text="Show QR Code"
                                           VerticalAlignment="Center"
                                           Foreground="White"
                                           FontSize="13" />
                            </StackPanel>
                        </Button>
```

The Barcode Fallback `StackPanel` should end like this after the edit:

```xml
                        <TextBlock x:Name="BarcodeStatusText"
                                   Text=""
                                   FontSize="11"
                                   TextWrapping="Wrap"
                                   Margin="0,6,0,0"
                                   Visibility="Collapsed" />

                        <Button Margin="0,10,0,0"
                                Style="{StaticResource ModernButton}"
                                Background="#7C3AED"
                                HorizontalAlignment="Left"
                                Click="ShowQrCode_Click"
                                ToolTip="Generate and view this employee's QR code">
                            <StackPanel Orientation="Horizontal">
                                <materialDesign:PackIcon Kind="Qrcode"
                                                         Width="16" Height="16"
                                                         Foreground="White"
                                                         VerticalAlignment="Center"
                                                         Margin="0,0,6,0" />
                                <TextBlock Text="Show QR Code"
                                           VerticalAlignment="Center"
                                           Foreground="White"
                                           FontSize="13" />
                            </StackPanel>
                        </Button>

                    </StackPanel>
                </Border>
```

- [ ] **Step 2: Add the click handler to AttendanceModal.xaml.cs**

In `AttendancePayrollSystem/AttendanceModal.xaml.cs`, add this method after the `ShowBarcodeStatus` method (around line 147):

```csharp
        private void ShowQrCode_Click(object sender, RoutedEventArgs e)
        {
            var modal = new QrCodeModal(_employee) { Owner = this };
            modal.ShowDialog();
        }
```

- [ ] **Step 3: Build to confirm no errors**

```bash
cd AttendancePayrollSystem
dotnet build -c Debug
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Run all tests**

```bash
cd AttendancePayrollSystem.Tests
dotnet test -v minimal
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add AttendancePayrollSystem/AttendanceModal.xaml AttendancePayrollSystem/AttendanceModal.xaml.cs
git commit -m "feat: add Show QR Code button to AttendanceModal barcode fallback"
```

---

## Manual Smoke Test

After all tasks are complete:

1. Run the app: `dotnet run --project AttendancePayrollSystem`
2. Log in as admin
3. Open any employee's Attendance Modal
4. Scroll to the **Barcode Fallback** section
5. Click **Show QR Code** — the `QrCodeModal` popup opens
6. Verify the QR image is displayed with the employee code and name in the header
7. Click **Save as PNG** — a `SaveFileDialog` appears; save and confirm the file exists on disk and opens correctly in an image viewer
8. Click **Print** — the `PrintDialog` appears
9. Click **Close** — the modal closes; the `AttendanceModal` remains open and functional
