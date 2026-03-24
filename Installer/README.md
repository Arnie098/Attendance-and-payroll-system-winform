# Installer

Build the Windows installer from the repository root:

```powershell
.\Installer\build-installer.ps1
```

The script publishes a self-contained `win-x64` build, stages the payload, generates WiX file entries, and outputs:

- `dist\AttendancePayrollSystem-Setup.msi`

The installer now shows a completion checkbox to launch the app after setup finishes in normal UI mode.

Optional flags:

```powershell
.\Installer\build-installer.ps1 -IncludeEnvFile
.\Installer\build-installer.ps1 -Configuration Release -Runtime win-x64
```

`-IncludeEnvFile` copies the current repo `.env` into the installer payload. Leave it off if you do not want database secrets embedded in the installer.
