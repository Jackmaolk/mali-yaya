param([switch]$ValidateOnly, [int]$ExcludeProcessId = 0, [string]$ExpectedAssemblyId = '')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$runtimePath = Join-Path $PSScriptRoot 'publish\widget-runtime.json'
$dll = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'publish\CodexUsageWidget.dll'))
$launcher = Join-Path $PSScriptRoot 'publish\CodexUsageWidget.exe'
$log = Join-Path $PSScriptRoot 'restart-widget.log'
$requestedAt = [DateTimeOffset]::UtcNow
function Write-Log([string]$text) {
    Add-Content -LiteralPath $log -Value ((Get-Date -Format s) + ' ' + $text) -Encoding UTF8
}
try {
    Write-Log 'Restart requested.'
    Add-Type -AssemblyName UIAutomationClient
    $session = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    $targets = @([System.Diagnostics.Process]::GetProcessesByName('dotnet') | Where-Object {
        if ($_.SessionId -ne $session -or $_.Id -eq $ExcludeProcessId) { return $false }
        try {
            return @($_.Modules | Where-Object {
                [string]::Equals($_.FileName, $dll, [StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0
        } catch { return $false }
    })
    $windows = @()
    foreach ($target in $targets) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$target.Id)
        $found = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children, $condition)
        $accessible = 0
        foreach ($window in $found) {
            $pattern = $null
            if ($window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$pattern)) {
                $windows += $pattern
                $accessible++
            }
        }
        if ($accessible -eq 0) { throw 'Cannot access widget window from this desktop. Run restart-widget.exe from your desktop session.' }
        Write-Log "Matched widget PID $($target.Id), started $($target.StartTime.ToString('s'))."
    }
    if ($ValidateOnly) { Write-Log 'Validation complete; no process changed.'; exit 0 }
    foreach ($window in $windows) {
        $window.Close()
    }
    foreach ($target in $targets) {
        if (-not $target.WaitForExit(10000)) { throw "Widget PID $($target.Id) did not close. Please close it and retry." }
    }
    Start-Process -FilePath $launcher -WorkingDirectory (Split-Path $launcher) -WindowStyle Hidden
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        Start-Sleep -Milliseconds 250
        if (-not (Test-Path -LiteralPath $runtimePath)) { continue }
        $state = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($state.buildVersion) -or -not $state.running -or
            $targets.Id -contains [int]$state.processId -or $null -eq $state.weeklyUsedPercent -or
            [DateTimeOffset]::Parse($state.updatedAtUtc) -lt $requestedAt -or
            ($ExpectedAssemblyId -and $state.assemblyId -ne $ExpectedAssemblyId)) { continue }
        Write-Log ("New runtime ready. Version={0}; Assembly={1}; PID={2}" -f
            $state.buildVersion, $state.assemblyId, $state.processId)
        exit 0
    }
    throw 'New runtime did not report its version and quota in time.'
}
catch {
    Write-Log ("ERROR: " + $_.Exception.Message)
    exit 1
}
