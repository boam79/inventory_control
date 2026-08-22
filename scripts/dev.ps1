# Restore, build, and test on Windows (and PowerShell 7 on other OSes).
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet not found. Install .NET 10 SDK from https://aka.ms/dotnet/download"
}

Write-Host "SDK: $(dotnet --version)  $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)"
dotnet restore --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build --nologo --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test Inventory.Tests/Inventory.Tests.csproj --nologo --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$onWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
if ($onWindows) {
    Write-Host "Windows: UI with  dotnet run --project Inventory.App"
    Write-Host "Installer: powershell -ExecutionPolicy Bypass -File scripts/pack-installer.ps1"
} else {
    Write-Host "This OS cannot run the WPF UI. Tests only."
}
