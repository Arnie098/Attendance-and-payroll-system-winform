# QR Code Modal — Design Spec

**Date:** 2026-06-27
**Scope:** Employee QR code generation popup in the Biometric Attendance Terminal (`AttendanceModal`)

---

## Summary

Add a **"Show QR Code"** button to the Barcode Fallback section of `AttendanceModal`. Clicking it opens a dedicated `QrCodeModal` popup that displays the employee's QR code (encoding their `EmployeeCode`), with options to save as PNG or print. This allows employees without a physical QR ID card to produce one on demand, or allows operators to display a QR for on-screen scanning.

---

## Architecture

### New files

| File | Purpose |
|------|---------|
| `AttendancePayrollSystem/Services/QrCodeService.cs` | Wraps QRCoder; generates `BitmapSource` and PNG bytes from an employee code |
| `AttendancePayrollSystem/QrCodeModal.xaml` | WPF popup window — QR image, employee label, Save/Print/Close buttons |
| `AttendancePayrollSystem/QrCodeModal.xaml.cs` | Code-behind: wires up `QrCodeService`, handles Save and Print actions |

### Modified files

| File | Change |
|------|--------|
| `AttendancePayrollSystem/AttendanceModal.xaml` | Add **"Show QR Code"** button to Barcode Fallback panel |
| `AttendancePayrollSystem/AttendanceModal.xaml.cs` | Handle button click — instantiate and show `QrCodeModal` |
| `AttendancePayrollSystem/AttendancePayrollSystem.csproj` | Add `QRCoder` NuGet package reference |

---

## QrCodeService

```
namespace AttendancePayrollSystem.Services

QrCodeService
  + BitmapSource Generate(string employeeCode, int pixelSize = 250)
  + byte[] GeneratePng(string employeeCode, int pixelSize = 400)
```

- Uses **QRCoder** (pure C#, MIT license, no native dependencies)
- QR content: the raw `EmployeeCode` string (e.g. `EMP-001`, `T-1001`)
- Error correction level: `M` (15% recovery — good balance for ID cards)
- Returns `BitmapSource` for WPF `Image` binding; `byte[]` for file save and print

---

## QrCodeModal

### Layout

```
┌─────────────────────────────────────────┐
│  [dark header]  Employee QR Code        │
│─────────────────────────────────────────│
│                                         │
│           ┌───────────────┐             │
│           │               │             │
│           │   [QR IMAGE]  │  250×250px  │
│           │               │             │
│           └───────────────┘             │
│                                         │
│        T-1001 · Ana Dela Cruz           │
│                                         │
│      [Save as PNG]    [Print]           │
│                                         │
│               [Close]                   │
└─────────────────────────────────────────┘
```

### Behavior

- **Constructor** receives an `Employee` object (has `EmployeeCode` and `FullName`)
- On `Loaded`: calls `QrCodeService.Generate(employee.EmployeeCode)` and binds result to the `Image` control
- **Save as PNG**: opens `SaveFileDialog` (default filename: `QR_{EmployeeCode}.png`); writes `QrCodeService.GeneratePng(...)` bytes to selected path
- **Print**: opens WPF `PrintDialog`; renders the `Image` element via `PrintVisual`
- **Close**: closes the window
- Styled with existing `MaterialDesignThemes` resources to match the rest of the app (white card on light background, `ModernButton` style for action buttons)
- `WindowStartupLocation="CenterOwner"`, `ResizeMode="NoResize"`, fixed size ~400×500

---

## Changes to AttendanceModal

- Add a **"Show QR Code"** `Button` below the existing scan input row in the Barcode Fallback `StackPanel`
- Styled as `ModernButton` with a `materialDesign:PackIcon Kind="Qrcode"` prefix icon
- Click handler in `AttendanceModal.xaml.cs`:

```csharp
private void ShowQrCode_Click(object sender, RoutedEventArgs e)
{
    var modal = new QrCodeModal(_employee) { Owner = this };
    modal.ShowDialog();
}
```

---

## Dependencies

| Package | Version | Source |
|---------|---------|--------|
| QRCoder | 1.6.0 | NuGet |

QRCoder has no native dependencies and targets `netstandard2.0`, fully compatible with `net8.0-windows`.

---

## Error Handling

- If `EmployeeCode` is null or empty, the modal shows an inline error label instead of the QR image and disables Save/Print buttons. This should never happen in practice (codes are required at registration) but is guarded defensively.
- Print and Save failures (e.g. disk full, printer offline) are caught and shown via `MessageBox`.

---

## Out of Scope

- Barcode formats other than QR (Code 128 etc.)
- QR code on the Employee Dashboard window
- Admin bulk-print for all employees
- Scanning the on-screen QR with the app itself (the existing USB scanner handles reading)
