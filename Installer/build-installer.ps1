param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "",
    [switch]$IncludeEnvFile
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 3.0

function Convert-ToWixId
{
    param(
        [string]$Value
    )

    $candidate = ($Value -replace '[^A-Za-z0-9_]', '_')
    if ($candidate -notmatch '^[A-Za-z_]') {
        $candidate = "_$candidate"
    }

    return $candidate
}

function New-ComponentXml
{
    param(
        [System.IO.FileInfo[]]$Files
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $null = $lines.Add('      <Component Id="ApplicationFilesComponent" Guid="CF1DB0A8-5355-4DDC-8B31-92F2935FBCC4">')
    $null = $lines.Add('        <CreateFolder />')

    foreach ($file in $Files)
    {
        $source = "!(bindpath.Payload)\$($file.Name)"
        if ($file.Name -eq "AttendancePayrollSystem.exe")
        {
            $null = $lines.Add("        <File Id=`"AppExecutable`" Source=`"$source`" />")
        }
        else
        {
            $fileId = Convert-ToWixId -Value ("File_" + $file.Name)
            $null = $lines.Add("        <File Id=`"$fileId`" Source=`"$source`" />")
        }
    }

    $null = $lines.Add('        <RegistryValue Root="HKCU" Key="Software\Sir Dvo\AttendancePayrollSystem" Name="InstallPath" Type="string" Value="[INSTALLFOLDER]" KeyPath="yes" />')
    $null = $lines.Add('        <RemoveFile Id="RemoveInstallFolderFiles" Name="*" On="uninstall" />')
    $null = $lines.Add('        <RemoveFolder Id="RemoveInstallFolder" Directory="INSTALLFOLDER" On="uninstall" />')
    $null = $lines.Add('      </Component>')
    return ($lines -join [Environment]::NewLine)
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "AttendancePayrollSystem\AttendancePayrollSystem.csproj"
$wixProjectPath = Join-Path $PSScriptRoot "AttendancePayrollSystem.Installer.wixproj"
$buildRoot = Join-Path $PSScriptRoot "build"
$publishDir = Join-Path $buildRoot "publish"
$payloadDir = Join-Path $buildRoot "payload"
$generatedWxsPath = Join-Path $buildRoot "ApplicationFiles.wxs"

if ([string]::IsNullOrWhiteSpace($OutputDir))
{
    $OutputDir = Join-Path $repoRoot "dist"
}

$outputDir = [System.IO.Path]::GetFullPath($OutputDir)
$outputInstaller = Join-Path $outputDir "AttendancePayrollSystem-Setup.msi"
$legacyExeInstaller = Join-Path $outputDir "AttendancePayrollSystem-Setup.exe"
$builtInstaller = Join-Path $PSScriptRoot "bin\$Configuration\AttendancePayrollSystem.Installer.msi"

Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $payloadDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $generatedWxsPath -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Remove-Item -LiteralPath $outputInstaller -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $legacyExeInstaller -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $builtInstaller -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $outputDir -Filter "~AttendancePayrollSystem*" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

& dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed."
}

$publishedExe = Join-Path $publishDir "AttendancePayrollSystem.exe"
if (-not (Test-Path $publishedExe))
{
    throw "Published executable was not created at $publishedExe"
}

$versionInfo = (Get-Item $publishedExe).VersionInfo
$productVersion = if ([string]::IsNullOrWhiteSpace($versionInfo.ProductVersion)) { "1.0.0" } else { $versionInfo.ProductVersion }
$productVersion = ($productVersion -split '[^0-9\.]')[0]
$versionParts = $productVersion -split '\.' | Where-Object { $_ -ne "" }
if ($versionParts.Count -ge 3)
{
    $productVersion = "$($versionParts[0]).$($versionParts[1]).$($versionParts[2])"
}

if ($productVersion -notmatch '^\d+\.\d+\.\d+$')
{
    $productVersion = "1.0.0"
}

Get-ChildItem -LiteralPath $publishDir -File |
    Where-Object { $_.Extension -ne ".pdb" } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $payloadDir $_.Name) -Force
    }

$envExamplePath = Join-Path $repoRoot ".env.example"
if (Test-Path $envExamplePath)
{
    Copy-Item -LiteralPath $envExamplePath -Destination (Join-Path $payloadDir ".env.example") -Force
}

$envPath = Join-Path $repoRoot ".env"
if ($IncludeEnvFile)
{
    if (-not (Test-Path $envPath))
    {
        throw "IncludeEnvFile was specified, but .env was not found at $envPath"
    }

    Copy-Item -LiteralPath $envPath -Destination (Join-Path $payloadDir ".env") -Force
}

$payloadFiles = Get-ChildItem -LiteralPath $payloadDir -File | Sort-Object Name
$appFileXml = New-ComponentXml -Files $payloadFiles

@"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="ApplicationFiles" Directory="INSTALLFOLDER">
$appFileXml
    </ComponentGroup>
  </Fragment>
</Wix>
"@ | Set-Content -LiteralPath $generatedWxsPath -Encoding ASCII

& dotnet build $wixProjectPath `
    -c $Configuration `
    -p:PayloadDir="$payloadDir" `
    -p:ProductVersion="$productVersion"
if ($LASTEXITCODE -ne 0)
{
    throw "Installer build failed."
}
if (-not (Test-Path $builtInstaller))
{
    throw "Installer build did not produce $builtInstaller"
}

Copy-Item -LiteralPath $builtInstaller -Destination $outputInstaller -Force
Write-Host "Installer created at $outputInstaller"
