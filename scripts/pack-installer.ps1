param(
    [string]$Version = "1.0.1"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$PublishDir = Join-Path $Root "publish"
$DistDir = Join-Path $Root "dist"
$PackId = "SpringClinic.Inventory"
$MainExe = "Inventory.App.exe"

New-Item -ItemType Directory -Force -Path $PublishDir, $DistDir | Out-Null
if (Test-Path $DistDir) {
    Get-ChildItem $DistDir -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
}

Write-Host "dotnet test"
dotnet test --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed" }

Write-Host "dotnet publish self-contained win-x64"
dotnet publish Inventory.App -c Release -r win-x64 --self-contained true -p:Version=$Version -o $PublishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "vpk pack"
dotnet tool restore | Out-Null
dotnet tool run vpk -- pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $PublishDir `
    --packTitle "스프링의원 재고관리" `
    --packAuthors "SpringClinic" `
    --mainExe $MainExe `
    --outputDir $DistDir `
    --shortcuts "Desktop,StartMenuRoot"
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

$setup = Get-ChildItem $DistDir -Filter "*Setup.exe" -Recurse | Select-Object -First 1
if (-not $setup) {
    $setup = Get-ChildItem $DistDir -Filter "*.exe" -Recurse | Where-Object { $_.Name -like "*Setup*" -or $_.Name -like "*setup*" } | Select-Object -First 1
}
if (-not $setup) {
    throw "Setup.exe was not created in $DistDir"
}

$hash = (Get-FileHash -Algorithm SHA256 $setup.FullName).Hash
$hashFile = Join-Path $DistDir "$($setup.BaseName).sha256"
Set-Content -Path $hashFile -Value "$hash  $($setup.Name)" -Encoding ascii

Write-Host "Installer: $($setup.FullName)"
Write-Host "SHA256: $hash"
Write-Host "Hash file: $hashFile"
Write-Host "Unsigned build: Windows SmartScreen may warn. Code signing certificate was not used."
