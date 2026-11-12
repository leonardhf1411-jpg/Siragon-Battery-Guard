$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = if (Test-Path (Join-Path $PSScriptRoot "src\SiragonBatteryGuard.cs")) { Join-Path $PSScriptRoot "src\SiragonBatteryGuard.cs" } else { Join-Path $PSScriptRoot "SiragonBatteryGuard.cs" }
$installerSrc = if (Test-Path (Join-Path $PSScriptRoot "src\Installer.cs")) { Join-Path $PSScriptRoot "src\Installer.cs" } else { Join-Path $PSScriptRoot "Installer.cs" }
$ico = if (Test-Path (Join-Path $PSScriptRoot "assets\battery.ico")) { Join-Path $PSScriptRoot "assets\battery.ico" } else { Join-Path $PSScriptRoot "battery.ico" }
$out = Join-Path $PSScriptRoot "SiragonBatteryGuard.exe"
$setupOut = Join-Path $PSScriptRoot "SiragonBatteryGuard_Setup.exe"
$old = Join-Path $PSScriptRoot "SiragonBatteryGuard.old.exe"

Get-Process -Name "SiragonBatteryGuard*" -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.CloseMainWindow() | Out-Null } catch {}
    try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch {}
}

if (Test-Path $old) {
    Remove-Item $old -Force -ErrorAction SilentlyContinue
}
if (Test-Path $out) {
    try {
        Remove-Item $out -Force -ErrorAction Stop
    } catch {
        Move-Item $out $old -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Compilando SiragonBatteryGuard.exe..." -ForegroundColor Cyan
& $csc /target:winexe /out:$out /win32icon:$ico /reference:System.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Windows.Forms.dll /optimize+ $src

if ($LASTEXITCODE -eq 0 -and (Test-Path $out)) {
    Write-Host "¡Compilación exitosa! Archivo generado: $out" -ForegroundColor Green
    if (Test-Path $installerSrc) {
        Write-Host "Compilando instalador SiragonBatteryGuard_Setup.exe..." -ForegroundColor Cyan
        & $csc /target:winexe /out:$setupOut /win32icon:$ico /resource:"$out,AppExe" /resource:"$ico,AppIcon" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /optimize+ $installerSrc
        if ($LASTEXITCODE -eq 0 -and (Test-Path $setupOut)) {
            Write-Host "¡Instalador compilado con éxito! Archivo generado: $setupOut" -ForegroundColor Green
        }
    }
} else {
    Write-Error "Error durante la compilación."
}
