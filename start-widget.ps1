$ErrorActionPreference = "Stop"
$publish = Join-Path $PSScriptRoot "publish"
$dll = Join-Path $publish "CodexUsageWidget.dll"
$launcher = Join-Path $publish "CodexUsageWidget.exe"
if (-not (Test-Path -LiteralPath $launcher) -or -not (Test-Path -LiteralPath $dll)) {
    & (Join-Path $PSScriptRoot "build.ps1")
}
$escapedDll = [regex]::Escape($dll)
$running = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ieq "dotnet.exe" -and $_.CommandLine -match $escapedDll }
if (-not $running) {
    Start-Process -FilePath $launcher -WorkingDirectory $publish -WindowStyle Hidden
}
