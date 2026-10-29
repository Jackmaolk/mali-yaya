param([switch]$ValidateOnly, [int]$ExcludeProcessId = 0, [string]$ExpectedAssemblyId = '')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$log = $null
$requestedAt = [DateTimeOffset]::UtcNow
function Assert-LocalPath([string]$Path, [switch]$Directory) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^[A-Za-z]:[\\/]' -or
        $Path.Substring(2).Contains(':')) { throw 'Absolute local drive path required.' }
    foreach ($part in ($Path.Substring(3) -split '[\\/]')) {
        if (-not $part -or $part -eq '.' -or $part -eq '..') { continue }
        if ($part.EndsWith('.') -or $part.EndsWith(' ') -or
            $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            ($part.Split('.')[0].TrimEnd(' ') -match '^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9\u00B9\u00B2\u00B3]|LPT[1-9\u00B9\u00B2\u00B3])$')) {
            throw 'Ambiguous or device path rejected.'
        }
    }
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $root.Length) { $full = $full.TrimEnd([char[]]'\/') }
    $drive = New-Object IO.DriveInfo($root)
    if ($drive.DriveType -in @([IO.DriveType]::Network, [IO.DriveType]::Unknown, [IO.DriveType]::NoRootDirectory)) {
        throw 'Local drive required.'
    }
    $item = $full
    $leaf = $true
    while ($item) {
        try {
            $attributes = [IO.File]::GetAttributes($item)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse path rejected.' }
            $isDirectory = ($attributes -band [IO.FileAttributes]::Directory) -ne 0
            if ((($Directory -or -not $leaf) -and -not $isDirectory) -or ($leaf -and -not $Directory -and $isDirectory)) {
                throw 'Unexpected storage path type.'
            }
        } catch [IO.FileNotFoundException] {
            # Missing leaf/ancestors are permitted; existing ancestors are still checked.
        } catch [IO.DirectoryNotFoundException] {
            # Continue walking up to the existing local parent.
        }
        $item = [IO.Path]::GetDirectoryName($item)
        $leaf = $false
    }
    return $full
}
function Write-Log([string]$text) {
    if (-not $log) { return }
    $null = Assert-LocalPath $log
    $parent = Assert-LocalPath ([IO.Path]::GetDirectoryName($log)) -Directory
    $null = [IO.Directory]::CreateDirectory($parent)
    $null = Assert-LocalPath $parent -Directory
    $previous = ''
    if ([IO.File]::Exists($log) -and (Get-Item -LiteralPath $log).Length -le 131072) {
        $previous = [IO.File]::ReadAllText($log)
    }
    $temporary = Assert-LocalPath ($log + '.tmp-' + [Guid]::NewGuid().ToString('N'))
    $created = $false
    try {
        $stream = New-Object IO.FileStream($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $created = $true
        try {
            $bytes = [Text.Encoding]::UTF8.GetBytes($previous + (Get-Date -Format s) + ' ' + $text + [Environment]::NewLine)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        } finally { $stream.Dispose() }
        $null = Assert-LocalPath $log
        $null = Assert-LocalPath $temporary
        if ([IO.File]::Exists($log)) { [IO.File]::Replace($temporary, $log, [NullString]::Value, $true) }
        else { [IO.File]::Move($temporary, $log) }
    } finally {
        if ($created) { $null = Assert-LocalPath $temporary; [IO.File]::Delete($temporary) }
    }
}
try {
    $installation = Assert-LocalPath (Join-Path $PSScriptRoot 'publish') -Directory
    $dll = Assert-LocalPath (Join-Path $installation 'CodexUsageWidget.dll')
    $launcher = Assert-LocalPath (Join-Path $installation 'CodexUsageWidget.exe')
    $dataRoot = [AppContext]::GetData('MaliYaya.DataDirectory')
    if ($null -eq $dataRoot) {
        $dataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexUsageWidget'
    } elseif ($dataRoot -isnot [string]) { throw 'Invalid data directory override.' }
    $dataRoot = Assert-LocalPath $dataRoot -Directory
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(
            $installation.TrimEnd([char[]]'\/').ToUpperInvariant()))).Replace('-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
    $runtimeDirectory = Assert-LocalPath (Join-Path (Join-Path $dataRoot 'runtime') $hash) -Directory
    $runtimePath = Assert-LocalPath (Join-Path $runtimeDirectory 'widget-runtime.json')
    $log = Assert-LocalPath (Join-Path $runtimeDirectory 'restart-widget.log')
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
        $null = Assert-LocalPath $runtimePath
        if (-not (Test-Path -LiteralPath $runtimePath)) { continue }
        if ((Get-Item -LiteralPath $runtimePath).Length -gt 8192) { continue }
        $state = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($state.buildVersion) -or -not $state.running -or
            $targets.Id -contains [int]$state.processId -or
            -not $state.moduleId -or [Guid]::Parse($state.moduleId) -eq [Guid]::Empty -or
            [DateTimeOffset]::Parse($state.updatedAtUtc) -lt $requestedAt -or
            ($ExpectedAssemblyId -and $state.moduleId -ne $ExpectedAssemblyId)) { continue }
        Write-Log ("New runtime ready. Version={0}; Assembly={1}; PID={2}" -f
            $state.buildVersion, $state.moduleId, $state.processId)
        exit 0
    }
    throw 'New runtime did not report its health and version in time.'
}
catch {
    Write-Log ("ERROR: " + $_.Exception.Message)
    exit 1
}
