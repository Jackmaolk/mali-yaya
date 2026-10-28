param([switch]$InstallDesktop)
$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "CodexUsageWidget.csproj"
$output = Join-Path $PSScriptRoot "publish"
dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:UseAppHost=false -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$icon = Join-Path $PSScriptRoot "Assets\mali-yaya.ico"
$launcherSource = Join-Path $PSScriptRoot "launcher\WidgetLauncher.cs"
$launcher = Join-Path $output "CodexUsageWidget.exe"
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /win32icon:$icon /out:$launcher $launcherSource
if ($LASTEXITCODE -ne 0) { throw "launcher compile failed with exit code $LASTEXITCODE" }

$restartSource = Join-Path $PSScriptRoot "launcher\RestartWidget.cs"
$restartHelper = Join-Path $PSScriptRoot "restart-widget.exe"
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /win32icon:$icon /out:$restartHelper $restartSource
if ($LASTEXITCODE -ne 0) { throw "restart helper compile failed with exit code $LASTEXITCODE" }

$desktopInstallerSource = Join-Path $PSScriptRoot "launcher\InstallDesktopEntry.cs"
$desktopInstaller = Join-Path $PSScriptRoot "InstallDesktopEntry.exe"
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /win32icon:$icon /out:$desktopInstaller $desktopInstallerSource
if ($LASTEXITCODE -ne 0) { throw "Desktop entry installer compile failed." }
if ($InstallDesktop) {
    Start-Process -FilePath $desktopInstaller -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -Wait
}
Write-Host "Published widget: $output"
Write-Host "Desktop entry is not changed unless -InstallDesktop is explicitly supplied."
