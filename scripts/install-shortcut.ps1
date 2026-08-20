# 폴더 배포본(zip)만 있을 때 시작 메뉴 바로가기를 만듭니다.
# DB는 %LOCALAPPDATA%\SpringClinicInventory\inventory.db 입니다.
param(
    [string]$PublishDir = ""
)

$ErrorActionPreference = "Stop"
if (-not $PublishDir) {
    $PublishDir = Split-Path -Parent $PSScriptRoot
    if (Test-Path (Join-Path (Split-Path -Parent $PSScriptRoot) "publish\Inventory.App.exe")) {
        $PublishDir = Join-Path (Split-Path -Parent $PSScriptRoot) "publish"
    }
}

$exe = Join-Path $PublishDir "Inventory.App.exe"
if (-not (Test-Path $exe)) {
    throw "Inventory.App.exe 를 찾을 수 없습니다: $exe"
}

$programs = [Environment]::GetFolderPath("Programs")
$shortcutPath = Join-Path $programs "스프링의원 재고관리.lnk"
$w = New-Object -ComObject WScript.Shell
$s = $w.CreateShortcut($shortcutPath)
$s.TargetPath = $exe
$s.WorkingDirectory = $PublishDir
$s.Description = "스프링의원 재고관리"
$s.Save()

Write-Host "바로가기: $shortcutPath"
Write-Host "DB 경로: $env:LOCALAPPDATA\SpringClinicInventory\inventory.db"
Write-Host "프로그램을 실행하려면 바로가기를 더블클릭하세요."
